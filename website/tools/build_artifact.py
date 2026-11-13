#!/usr/bin/env python3
"""Собирает фрагмент страницы для публикации в виде артефакта: без doctype/html/head/body.
Использование: python3 -I tools/build_artifact.py <куда писать>"""
import re, sys, pathlib

root = pathlib.Path(__file__).resolve().parent.parent
src = (root / "index.html").read_text(encoding="utf8")
title = "<title>Astropolity</title>"
links = [m for m in re.findall(r"<link [^>]*>", src) if "preconnect" not in m or "gstatic" in m]
body = re.search(r"<body>(.*)</body>", src, re.S).group(1)
out = title + "\n" + "\n".join(links) + "\n" + body.strip() + "\n"
pathlib.Path(sys.argv[1]).write_text(out, encoding="utf8")
print("ok", len(out), "bytes")
