"""Abre o dashboard e grava as leituras da API industrial no SQLite existente."""

import argparse
import csv
import hashlib
import io
import json
import math
import sqlite3
import sys
import threading
import time
import webbrowser
from contextlib import closing
from datetime import date, datetime, time as clock_time, timedelta, timezone
from http.client import HTTPException
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlsplit

import coletor_sqlite as coletor


PASTA = Path(__file__).resolve().parent
FUSO = timezone(timedelta(hours=-3))
JANELA = timedelta(minutes=30)
CAMPOS = ", ".join(
    "id_origem AS id" if c == "id" else
    "COALESCE(coletado_em, gravado_em) AS coletado_em" if c == "coletado_em" else c
    for c in coletor.CAMPOS_CSV
)


def limites_dia(dia):
    inicio = datetime.combine(dia, clock_time.min, tzinfo=FUSO)
    return coletor.iso_utc(inicio), coletor.iso_utc(inicio + timedelta(days=1))


def dia_local(timestamp):
    return datetime.fromisoformat(timestamp.replace("Z", "+00:00")).astimezone(FUSO).date()


class Painel:
    def __init__(self, banco):
        self.banco = banco.resolve()
        self.lock = threading.Lock()
        self.estado = {"estado": "iniciando", "erro": None}
        self.cache_dias = (0, ())
        self.parar = threading.Event()

    def coletar(self, url, intervalo):
        with closing(coletor.abrir_banco(self.banco)) as db:
            while not self.parar.is_set():
                inicio = time.monotonic()
                try:
                    lote = coletor.baixar_leituras(url, timeout=5)
                    resultado = coletor.salvar(db, lote, "api")
                    with self.lock:
                        self.estado = {"estado": "online", "erro": None}
                    coletor.relatar("API", resultado)
                except (OSError, HTTPException, ValueError, sqlite3.Error) as erro:
                    with self.lock:
                        self.estado = {"estado": "offline", "erro": str(erro)}
                    print(f"Falha na coleta: {erro}. Tentando novamente.", flush=True)
                self.parar.wait(max(0.2, intervalo - (time.monotonic() - inicio)))

    def consultar(self, periodo):
        ao_vivo = periodo == "ao-vivo"
        dia = None if ao_vivo else date.fromisoformat(periodo)
        if dia and dia.isoformat() != periodo:
            raise ValueError("Use uma data no formato AAAA-MM-DD.")
        agora = datetime.now(timezone.utc)
        with closing(sqlite3.connect(self.banco.as_uri() + "?mode=ro", uri=True, timeout=5)) as db:
            db.execute("BEGIN")
            ultima = db.execute("SELECT MAX(timestamp) FROM leituras").fetchone()[0]
            ultimo_dia = dia_local(ultima) if ultima else None
            if ao_vivo:
                inicio = coletor.iso_utc(agora - JANELA)
                fim = coletor.iso_utc(agora + timedelta(minutes=5))
            else:
                inicio, fim = limites_dia(dia)
            sql = f"SELECT {CAMPOS} FROM leituras WHERE timestamp >= ? AND timestamp < ? ORDER BY timestamp, leitura_id"
            linhas = db.execute(sql, (inicio, fim)).fetchall()
            dia_exibido = None
            if ao_vivo and not linhas and ultimo_dia:
                dia_exibido = ultimo_dia.isoformat()
                linhas = db.execute(sql, limites_dia(ultimo_dia)).fetchall()
            with self.lock:
                expiracao, dias = self.cache_dias
                if time.monotonic() >= expiracao:
                    dias = tuple(r[0] for r in db.execute(
                        "SELECT DISTINCT date(timestamp, '-3 hours') AS dia FROM leituras ORDER BY dia DESC"
                    ) if r[0])
                    self.cache_dias = (time.monotonic() + 30, dias)
                estado = self.estado.copy()
        dias = set(dias)
        if ultimo_dia:
            dias.add(ultimo_dia.isoformat())
        if dia:
            dias.add(dia.isoformat())
        arquivo = io.StringIO(newline="")
        escritor = csv.writer(arquivo, delimiter=";")
        escritor.writerow(coletor.CAMPOS_CSV)
        escritor.writerows(linhas)
        dados = arquivo.getvalue()
        return dict(estado, dias=sorted(dias, reverse=True), csv=dados, dia_exibido=dia_exibido,
                    ultima_leitura=ultima, versao=hashlib.sha256(dados.encode("utf-8")).hexdigest())


def criar_handler(painel, html):
    class Handler(BaseHTTPRequestHandler):
        def enviar(self, codigo, dados, tipo):
            self.send_response(codigo)
            self.send_header("Content-Type", tipo)
            self.send_header("Content-Length", str(len(dados)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.end_headers()
            try:
                self.wfile.write(dados)
            except (BrokenPipeError, ConnectionResetError):
                pass

        def json(self, codigo, dados):
            self.enviar(codigo, json.dumps(dados, ensure_ascii=False).encode("utf-8"),
                        "application/json; charset=utf-8")

        def do_GET(self):
            porta = self.server.server_port
            if self.headers.get("Host") not in (f"127.0.0.1:{porta}", f"localhost:{porta}"):
                self.json(403, {"erro": "Este painel só aceita acesso local."})
                return
            rota = urlsplit(self.path)
            if rota.path == "/":
                self.enviar(200, html, "text/html; charset=utf-8")
            elif rota.path == "/api/painel":
                try:
                    periodo = parse_qs(rota.query).get("periodo", ["ao-vivo"])[0]
                    self.json(200, painel.consultar(periodo))
                except (ValueError, OverflowError):
                    self.json(400, {"erro": "Período inválido."})
                except sqlite3.Error as erro:
                    print(f"SQLite: {erro}", file=sys.stderr, flush=True)
                    self.json(503, {"erro": "Não foi possível consultar o banco."})
            else:
                self.json(404, {"erro": "Página não encontrada."})

        def log_message(self, *_):
            pass

    return Handler


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", help="endereço da API industrial")
    parser.add_argument("--intervalo", type=float, default=5, help="segundos entre coletas")
    parser.add_argument("--porta", type=int, default=8765, help="porta local do dashboard")
    parser.add_argument("--banco", type=Path, default=coletor.BANCO)
    parser.add_argument("--sem-navegador", action="store_true")
    args = parser.parse_args()
    if not math.isfinite(args.intervalo) or args.intervalo <= 0:
        parser.error("--intervalo precisa ser um número positivo.")
    if not 0 <= args.porta <= 65535:
        parser.error("--porta deve estar entre 0 e 65535.")
    try:
        endereco = args.api if args.api is not None else input("Endereço atual da API industrial: ")
        if not endereco.strip():
            raise ValueError("Informe o mesmo endereço de API que você usa no coletor.")
        url_api = coletor.montar_url(endereco)
        html = (PASTA / "painel.html").read_bytes()
        painel = Painel(args.banco)
        with ThreadingHTTPServer(("127.0.0.1", args.porta), criar_handler(painel, html)) as servidor:
            with closing(coletor.abrir_banco(painel.banco)):
                pass
            trabalhador = threading.Thread(target=painel.coletar, args=(url_api, args.intervalo), daemon=True)
            trabalhador.start()
            url_local = f"http://127.0.0.1:{servidor.server_port}"
            print(f"Painel: {url_local}\nAPI: {url_api}\nBanco: {painel.banco}\nCtrl+C para encerrar.", flush=True)
            if not args.sem_navegador:
                threading.Thread(target=webbrowser.open, args=(url_local,), daemon=True).start()
            try:
                servidor.serve_forever()
            finally:
                painel.parar.set()
                trabalhador.join(timeout=6)
    except KeyboardInterrupt:
        print("\nPainel e coleta encerrados.")
    except (OSError, ValueError, EOFError, sqlite3.Error) as erro:
        print(f"Erro ao iniciar o painel: {erro}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
