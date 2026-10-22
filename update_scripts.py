import sys

# 1. Update GripperBridge.cs
path_gb = r"C:\Users\dhamm\VR\Assets\Scripts\GripperBridge.cs"
with open(path_gb, "r", encoding="utf-8") as f:
    content_gb = f.read()

# Add shakeRequested field if not already present
if "private bool shakeRequested = false;" not in content_gb:
    old_decl = "private bool homeRequested = false;"
    new_decl = "private bool homeRequested = false;\n        private bool shakeRequested = false;"
    assert old_decl in content_gb, "homeRequested not found"
    content_gb = content_gb.replace(old_decl, new_decl, 1)

# Add RequestShake method if not present
if "public void RequestShake()" not in content_gb:
    idx = content_gb.find("public void RequestHome()")
    assert idx != -1, "RequestHome not found"
    end_idx = content_gb.find("}", idx)
    end_idx = content_gb.find("}", end_idx + 1)
    meth_chunk = content_gb[idx:end_idx+1]
    new_meth_chunk = meth_chunk + """

        public void RequestShake()
        {
            lock (lockObj)
            {
                shakeRequested = true;
            }
            Debug.Log("[GripperBridge] RequestShake: Sending SHAKE command to UR3e robot...");
        }"""
    content_gb = content_gb.replace(meth_chunk, new_meth_chunk, 1)

# Add sending SHAKE in NetworkLoop
if 'Encoding.ASCII.GetBytes("SHAKE\\n")' not in content_gb:
    old_send = """                        if (shouldHome)
                        {
                            byte[] homeBytes = Encoding.ASCII.GetBytes("HOME\\n");
                            stream.Write(homeBytes, 0, homeBytes.Length);
                            stream.Flush();
                        }"""
    new_send = """                        if (shouldHome)
                        {
                            byte[] homeBytes = Encoding.ASCII.GetBytes("HOME\\n");
                            stream.Write(homeBytes, 0, homeBytes.Length);
                            stream.Flush();
                        }

                        // Send SHAKE command to UR3e robot for automatic tube mixing
                        bool shouldShake = false;
                        lock (lockObj)
                        {
                            if (shakeRequested)
                            {
                                shouldShake = true;
                                shakeRequested = false;
                            }
                        }
                        if (shouldShake)
                        {
                            byte[] shakeBytes = Encoding.ASCII.GetBytes("SHAKE\\n");
                            stream.Write(shakeBytes, 0, shakeBytes.Length);
                            stream.Flush();
                        }"""
    assert old_send in content_gb, "old_send not found"
    content_gb = content_gb.replace(old_send, new_send, 1)

with open(path_gb, "w", encoding="utf-8") as f:
    f.write(content_gb)

print("Updated GripperBridge.cs successfully")
