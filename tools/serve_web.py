from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import os
import sys

BUILD = Path(__file__).resolve().parents[1] / 'unity' / 'PryingEyes' / 'Builds' / 'Web'

class Handler(SimpleHTTPRequestHandler):
    def end_headers(self):
        if self.path.split('?', 1)[0].endswith('.gz'):
            self.send_header('Content-Encoding', 'gzip')
        super().end_headers()

    def guess_type(self, path):
        if path.endswith('.wasm.gz'):
            return 'application/wasm'
        if path.endswith('.js.gz'):
            return 'application/javascript'
        return super().guess_type(path)

if __name__ == '__main__':
    os.chdir(BUILD)
    host = '0.0.0.0' if '--lan' in sys.argv else '127.0.0.1'
    print('Build Web su http://localhost:8000 - Ctrl+C per fermare', flush=True)
    ThreadingHTTPServer((host, 8000), Handler).serve_forever()
