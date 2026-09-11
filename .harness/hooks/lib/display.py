"""Spelling a path into a line a reader reads whole."""

import json


def displayed(path):
    """`path` quoted and escaped where a character in it would stop the line reading as written."""
    return path if path.isprintable() else json.dumps(path)
