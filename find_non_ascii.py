import os

scripts_dir = r'C:\Users\dhamm\VR\Assets\Scripts'
for root, dirs, files in os.walk(scripts_dir):
    for f in files:
        if f.endswith('.cs'):
            fpath = os.path.join(root, f)
            with open(fpath, 'rb') as fp:
                raw = fp.read()
            text = raw.decode('utf-8', errors='replace')
            for line_idx, line in enumerate(text.splitlines()):
                code_part = line.split('//')[0]
                if '"' in code_part and any(ord(c) > 127 for c in code_part):
                    non_ascii_chars = [c for c in code_part if ord(c) > 127]
                    print(f"{f}:{line_idx+1}: {repr(code_part.strip())}")
