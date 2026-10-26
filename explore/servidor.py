"""Servidor local do painel exploratório.

Entrega o exploracao.html e repassa as requisições /api/* para a API industrial.
Como o painel e os dados passam a vir do mesmo endereço, o navegador não aplica
o bloqueio de CORS.

Uso:
    python3 servidor.py
    python3 servidor.py --api http://10.107.67.92:8080 --porta 8000

Depois abra http://localhost:8000 no navegador.
"""
import argparse
import json
import os
import urllib.error
import urllib.request
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

PASTA = os.path.dirname(os.path.abspath(__file__))


def criar_handler(api_base):
    class Handler(SimpleHTTPRequestHandler):
        def __init__(self, *args, **kwargs):
            super().__init__(*args, directory=PASTA, **kwargs)

        def do_GET(self):
            if self.path in ("/", "/index.html"):
                self.send_response(302)
                self.send_header("Location", "/exploracao.html")
                self.end_headers()
            elif self.path.startswith("/api/"):
                self.repassar(self.path[len("/api"):])
            else:
                super().do_GET()

        def repassar(self, caminho):
            try:
                # Menor que o limite de 8 s do painel, para a resposta de erro chegar antes de ele desistir.
                with urllib.request.urlopen(api_base + caminho, timeout=5) as resp:
                    self.responder(resp.status, resp.read(), resp.headers.get("Content-Type", "application/json"))
            except urllib.error.HTTPError as e:
                self.responder(e.code, e.read(), e.headers.get("Content-Type", "application/json"))
            except (urllib.error.URLError, TimeoutError, OSError) as e:
                motivo = getattr(e, "reason", e)
                mensagem = f"API inacessível em {api_base}: {motivo}"
                print(f"[{self.log_date_time_string()}] {mensagem}", flush=True)
                self.responder(502, json.dumps({"erro": mensagem}).encode(), "application/json")

        def responder(self, status, corpo, tipo):
            try:
                self.send_response(status)
                self.send_header("Content-Type", tipo)
                self.send_header("Cache-Control", "no-store")
                # Permite que o exploracao.html aberto como arquivo (origin null) também leia o proxy.
                self.send_header("Access-Control-Allow-Origin", "*")
                self.end_headers()
                self.wfile.write(corpo)
            except (BrokenPipeError, ConnectionResetError):
                pass  # o navegador desistiu da requisição antes da resposta; não há o que entregar

        def log_message(self, formato, *args):
            if not self.path.startswith("/api/"):
                super().log_message(formato, *args)

    return Handler


def main():
    parser = argparse.ArgumentParser(description="Servidor local do painel exploratório (proxy da API industrial).")
    parser.add_argument("--api", default="http://10.107.67.92:8080", help="URL base da API industrial")
    parser.add_argument("--porta", type=int, default=8000, help="porta local do painel")
    args = parser.parse_args()

    api_base = args.api.rstrip("/")
    servidor = ThreadingHTTPServer(("127.0.0.1", args.porta), criar_handler(api_base))
    print(f"Painel em http://localhost:{args.porta}  →  API {api_base}")
    print("Ctrl+C para encerrar.")
    try:
        servidor.serve_forever()
    except KeyboardInterrupt:
        print("\nEncerrado.")


if __name__ == "__main__":
    main()
