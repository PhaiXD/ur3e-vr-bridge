import os
import re

log_path = os.path.expandvars(r"%LOCALAPPDATA%\Unity\Editor\Editor.log")
with open(log_path, "r", encoding="utf-8", errors="ignore") as f:
    lines = f.readlines()

print(f"Total lines in Editor.log: {len(lines)}")

# Search last 2000 lines
last_lines = lines[-2000:]
error_lines = []
for i, line in enumerate(last_lines):
    if re.search(r'error CS\d+', line) or "error CS" in line or "Compilation failed" in line or ": error " in line:
        # print surrounding context
        start = max(0, i - 2)
        end = min(len(last_lines), i + 3)
        error_lines.append("".join(last_lines[start:end]))

print(f"Found {len(error_lines)} error blocks:")
for block in error_lines[-10:]:
    print("--- BLOCK ---")
    print(block)
