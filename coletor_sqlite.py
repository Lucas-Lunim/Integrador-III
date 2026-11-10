"""Coleta a API industrial do Integrador III e guarda o histórico no SQLite."""

import argparse
import csv
import json
import math
import sqlite3
import sys
import time
from contextlib import closing
from datetime import datetime, timezone
from http.client import HTTPException
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import Request, urlopen


PASTA = Path(__file__).resolve().parent
BANCO = PASTA / "leituras_industriais.db"
METRICAS = {
    "temperatura": "REAL", "vibracao": "REAL", "rotacao": "REAL",
    "producao": "INTEGER", "defeitos": "INTEGER", "desgaste": "REAL",
    "pressao": "REAL", "ciclos": "INTEGER", "consumo_kw": "REAL",
    "horas_operacao": "REAL", "falhas": "INTEGER", "velocidade": "REAL",
    "carga": "REAL", "paradas": "INTEGER", "corrente": "REAL",
    "rejeitados": "INTEGER",
}
CAMPOS_CSV = (
    "id", "timestamp", "maquina", "tipo", "temperatura", "vibracao",
    "rotacao", "producao", "defeitos", "desgaste", "status", "coletado_em",
    "pressao", "ciclos", "consumo_kw", "horas_operacao", "falhas",
    "velocidade", "carga", "paradas", "corrente", "rejeitados",
)


def iso_utc(data):
    return data.astimezone(timezone.utc).isoformat(timespec="microseconds").replace("+00:00", "Z")


def agora_utc():
    return iso_utc(datetime.now(timezone.utc))


def timestamp(valor):
    if not isinstance(valor, str):
        raise ValueError("timestamp ausente ou inválido")
    valor = valor.strip()
    data = datetime.fromisoformat(valor[:-1] + "+00:00" if valor.endswith("Z") else valor)
    if data.tzinfo is None:
        raise ValueError("timestamp sem fuso horário")
    return iso_utc(data)


def texto(valor):
    return (valor.strip() or None) if isinstance(valor, str) else None


def numero(valor, tipo):
    if isinstance(valor, bool):
        return None
    if isinstance(valor, str):
        valor = valor.strip().replace(",", ".")
        if tipo == "INTEGER":
            try:
                valor = int(valor)
            except ValueError:
                pass
    try:
        n = valor if isinstance(valor, int) else float(valor)
    except (TypeError, ValueError, OverflowError):
        return None
    if tipo == "INTEGER" and isinstance(n, int):
        return n if -(2**63) <= n < 2**63 else None
    try:
        if not math.isfinite(n):
            return None
        if tipo == "INTEGER" and n.is_integer() and -(2**63) <= n < 2**63:
            return int(n)
        return float(n) if tipo == "REAL" else None
    except OverflowError:
        return None


def abrir_banco(caminho):
    db = sqlite3.connect(caminho, timeout=10)
    try:
        db.execute("PRAGMA foreign_keys = ON")
        db.execute("PRAGMA synchronous = FULL")
        colunas = ", ".join(f"{nome} {tipo}" for nome, tipo in METRICAS.items())
        db.executescript(f"""
            CREATE TABLE IF NOT EXISTS leituras (
                leitura_id INTEGER PRIMARY KEY,
                id_origem TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                maquina TEXT NOT NULL,
                tipo TEXT,
                status TEXT,
                coletado_em TEXT,
                gravado_em TEXT NOT NULL,
                {colunas},
                UNIQUE (maquina, id_origem, timestamp)
            );
            CREATE INDEX IF NOT EXISTS idx_leituras_maquina_data
                ON leituras (maquina, timestamp);
            CREATE INDEX IF NOT EXISTS idx_leituras_data ON leituras (timestamp);
            CREATE TABLE IF NOT EXISTS originais (
                leitura_id INTEGER NOT NULL REFERENCES leituras(leitura_id),
                origem TEXT NOT NULL,
                json_original TEXT NOT NULL,
                gravado_em TEXT NOT NULL,
                PRIMARY KEY (leitura_id, origem, json_original)
            );
            CREATE TABLE IF NOT EXISTS registros_invalidos (
                origem TEXT NOT NULL,
                json_original TEXT NOT NULL,
                motivo TEXT NOT NULL,
                gravado_em TEXT NOT NULL,
                PRIMARY KEY (origem, json_original)
            );
        """)
        return db
    except BaseException:
        db.close()
        raise


EXTRAS = ("tipo", "status", "coletado_em", *METRICAS)
COLUNAS = ("maquina", "id_origem", "timestamp", "gravado_em", *EXTRAS)
INSERIR = (
    f"INSERT INTO leituras ({', '.join(COLUNAS)}) "
    f"VALUES ({', '.join('?' for _ in COLUNAS)}) "
    "ON CONFLICT (maquina, id_origem, timestamp) DO NOTHING"
)
PREENCHER = (
    "UPDATE leituras SET "
    + ", ".join(f"{c} = COALESCE({c}, :{c})" for c in EXTRAS)
    + " WHERE leitura_id = :leitura_id AND ("
    + " OR ".join(f"({c} IS NULL AND :{c} IS NOT NULL)" for c in EXTRAS)
    + ")"
)


def salvar(db, registros, origem):
    novos = repetidos = invalidos = 0
    gravado = agora_utc()
    # Confirma a transação do lote inteiro antes de reportar sucesso.
    with db:
        for item in registros:
            bruto = json.dumps(item, ensure_ascii=True, sort_keys=True, separators=(",", ":"))
            try:
                if not isinstance(item, dict):
                    raise ValueError("a leitura não é um objeto")
                maquina = texto(item.get("maquina")) or texto(item.get("machine"))
                id_origem = item.get("id")
                if isinstance(id_origem, int) and not isinstance(id_origem, bool):
                    id_origem = str(id_origem)
                else:
                    id_origem = texto(id_origem)
                if not maquina or not id_origem:
                    raise ValueError("faltou o identificador da máquina ou da leitura")
                chave = (maquina, id_origem, timestamp(item.get("timestamp")))
            except (ValueError, OverflowError) as erro:
                db.execute(
                    "INSERT INTO registros_invalidos VALUES (?, ?, ?, ?) "
                    "ON CONFLICT (origem, json_original) DO NOTHING",
                    (origem, bruto, str(erro), gravado),
                )
                invalidos += 1
                continue
            campos = {
                "tipo": texto(item.get("tipo")),
                "status": texto(item.get("status")),
                "coletado_em": texto(item.get("coletado_em")),
                **{nome: numero(item.get(nome), tipo) for nome, tipo in METRICAS.items()},
            }
            cur = db.execute(INSERIR, (*chave, gravado, *(campos[c] for c in EXTRAS)))
            if cur.rowcount:
                leitura_id = cur.lastrowid
                novos += 1
            else:
                leitura_id = db.execute(
                    "SELECT leitura_id FROM leituras WHERE maquina=? AND id_origem=? AND timestamp=?",
                    chave,
                ).fetchone()[0]
                db.execute(PREENCHER, dict(campos, leitura_id=leitura_id))
                repetidos += 1
            # Guarda campos extras e versões diferentes sem multiplicar as leituras na consulta.
            db.execute(
                "INSERT INTO originais VALUES (?, ?, ?, ?) "
                "ON CONFLICT (leitura_id, origem, json_original) DO NOTHING",
                (leitura_id, origem, bruto, gravado),
            )
    return novos, repetidos, invalidos


def ler_csv(caminho):
    with caminho.open(encoding="utf-8-sig", newline="") as arquivo:
        leitor = csv.reader(arquivo, delimiter=";", strict=True)
        colunas = [c.strip() for c in next(leitor, [])]
        if (not {"id", "timestamp", "maquina"}.issubset(colunas)
                or "" in colunas or len(set(colunas)) != len(colunas)):
            raise ValueError(f"{caminho.name}: cabeçalho CSV inválido (esperado separador ';').")
        for valores in leitor:
            if not valores or not any(valores):
                continue
            if len(valores) > len(colunas):
                yield {"linha_csv": leitor.line_num, "colunas": colunas, "valores": valores}
            else:
                yield dict(zip(colunas, valores))


def montar_url(base):
    base = base.strip().rstrip("/")
    if "://" not in base:
        base = "http://" + base
    partes = urlsplit(base)
    if (partes.scheme not in ("http", "https") or not partes.hostname
            or partes.query or partes.fragment or partes.username is not None
            or any(c.isspace() for c in base)):
        raise ValueError("Informe o endereço da API, como http://IP_DO_SERVIDOR:8080.")
    if partes.port is None and partes.scheme == "http":
        base = partes._replace(netloc=partes.netloc + ":8080").geturl()
    return base if partes.path.endswith("/maquinas") else base + "/maquinas"


def baixar_leituras(url, timeout):
    pedido = Request(url, headers={"Accept": "application/json", "Cache-Control": "no-cache"})
    with urlopen(pedido, timeout=timeout) as resposta:
        dados = json.load(resposta)
    if isinstance(dados, dict):
        dados = dados.get("dados")
    if not isinstance(dados, list):
        raise ValueError("A API deve retornar uma lista de leituras ou um objeto com a lista em 'dados'.")
    return dados


def relatar(origem, resultado):
    novas, repetidas, invalidas = resultado
    print(f"{datetime.now():%H:%M:%S} | {origem}: {novas} novas, "
          f"{repetidas} já registradas, {invalidas} inválidas.", flush=True)
    if invalidas:
        print("  Consulte registros_invalidos para recuperar os dados fora do formato esperado.", flush=True)


def coletar(db, url, intervalo, uma_vez):
    print(f"Coletando de {url}. Ctrl+C para encerrar.", flush=True)
    teve_leituras = False
    while True:
        inicio = time.monotonic()
        try:
            lote = baixar_leituras(url, timeout=5)
            resultado = salvar(db, lote, "api")
            relatar("API", resultado)
            novas, repetidas, _ = resultado
            if teve_leituras and novas and not repetidas:
                print("  O lote não contém leituras já vistas. Pode ter havido uma lacuna na coleta.", flush=True)
            teve_leituras = bool(novas + repetidas) or teve_leituras
            if uma_vez:
                return 0
        except (OSError, HTTPException, ValueError, sqlite3.Error) as erro:
            print(f"{datetime.now():%H:%M:%S} | Falha na coleta: {erro}", file=sys.stderr, flush=True)
            if uma_vez:
                return 1
        time.sleep(max(0.2, intervalo - (time.monotonic() - inicio)))


def exportar(db, caminho):
    colunas = [
        "id_origem AS id" if c == "id" else
        "COALESCE(coletado_em, gravado_em) AS coletado_em" if c == "coletado_em" else c
        for c in CAMPOS_CSV
    ]
    linhas = db.execute(f"SELECT {', '.join(colunas)} FROM leituras ORDER BY timestamp, leitura_id")
    with caminho.open("x", encoding="utf-8-sig", newline="") as arquivo:
        escritor = csv.writer(arquivo, delimiter=";")
        escritor.writerow(CAMPOS_CSV)
        escritor.writerows(linhas)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--banco", type=Path, default=BANCO, help="caminho do banco SQLite")
    comandos = parser.add_subparsers(dest="comando", required=True)
    coleta = comandos.add_parser("coletar", help="consultar a API e gravar continuamente")
    coleta.add_argument("--api", help="endereço da API; solicitado se omitido")
    coleta.add_argument("--intervalo", type=float, default=5, help="segundos entre consultas (padrão: 5)")
    coleta.add_argument("--uma-vez", action="store_true", help="faz somente uma consulta")
    comandos.add_parser("importar", help="adicionar CSVs antigos ao histórico").add_argument(
        "arquivos", nargs="+", type=Path
    )
    comandos.add_parser("exportar", help="gerar CSV para o dashboard").add_argument("arquivo", type=Path)
    comandos.add_parser("backup", help="criar uma cópia consistente do SQLite").add_argument("arquivo", type=Path)
    args = parser.parse_args()
    try:
        if args.comando == "coletar":
            if not math.isfinite(args.intervalo) or args.intervalo <= 0:
                parser.error("--intervalo precisa ser um número positivo.")
            base = args.api if args.api is not None else input("Endereço da API (IP ou URL): ")
            if not base.strip():
                raise ValueError("Informe o endereço usado pelo painel exploratório.")
            url = montar_url(base)
        if args.comando in ("exportar", "backup") and not args.banco.is_file():
            raise FileNotFoundError(f"Banco não encontrado: {args.banco}")
        with closing(abrir_banco(args.banco)) as db:
            print(f"Banco: {args.banco.resolve()}", flush=True)
            if args.comando == "coletar":
                return coletar(db, url, args.intervalo, args.uma_vez)
            if args.comando == "importar":
                for arquivo in args.arquivos:
                    relatar(arquivo.name, salvar(db, ler_csv(arquivo), "csv"))
                total = db.execute("SELECT COUNT(*) FROM leituras").fetchone()[0]
                print(f"Total no banco: {total} leituras.")
            elif args.comando == "exportar":
                exportar(db, args.arquivo)
                print(f"CSV: {args.arquivo.resolve()}")
            else:
                with args.arquivo.open("xb"):
                    pass
                with closing(sqlite3.connect(args.arquivo)) as copia:
                    db.backup(copia)
                print(f"Backup: {args.arquivo.resolve()}")
        return 0
    except KeyboardInterrupt:
        print("\nColeta encerrada. Os lotes confirmados continuam salvos.")
        return 0
    except (OSError, ValueError, EOFError, csv.Error, sqlite3.Error) as erro:
        print(f"Erro: {erro}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
