#!/usr/bin/env python3
"""Serve actual test registry snapshots with Prometheus' required media type."""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

class MetricSnapshotHandler(SimpleHTTPRequestHandler):
    extensions_map = dict(SimpleHTTPRequestHandler.extensions_map,
                          **{'.prom': 'text/plain; version=0.0.4; charset=utf-8'})

if __name__ == '__main__':
    ThreadingHTTPServer(('0.0.0.0', 8080), partial(MetricSnapshotHandler, directory='/metrics')).serve_forever()
