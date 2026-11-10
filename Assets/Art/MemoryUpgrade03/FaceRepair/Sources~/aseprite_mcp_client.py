"""Run the installed Aseprite MCP over stdio; preserve receipts for reproducibility."""
import asyncio
import datetime
import json
import os
import sys
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

ROOT = Path(__file__).resolve().parent
SERVER = Path(r"C:\Users\Administrator\.codex\aseprite-mcp")


async def main():
    params = StdioServerParameters(
        command=str(SERVER / ".venv/Scripts/python.exe"),
        args=["-m", "aseprite_mcp"],
        cwd=str(SERVER),
        env=dict(os.environ, ASEPRITE_PATH=r"H:\SteamLibrary\steamapps\common\Aseprite\Aseprite.exe",
                 PYTHONUTF8="1", PYTHONIOENCODING="utf-8"),
    )
    script = (ROOT / sys.argv[1]).read_text(encoding="utf-8")
    # MCP executes source text, so Lua cannot derive its original file location.
    source_root = json.dumps(ROOT.as_posix() + "/", ensure_ascii=False)
    script = "_G.REPAIR_BUST_SOURCE_ROOT = " + source_root + "\n" + script
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            result = await session.call_tool("run_lua_script", {"script": script},
                                             read_timeout_seconds=datetime.timedelta(seconds=600))
            with (ROOT / "mcp_receipts.jsonl").open("a", encoding="utf-8") as receipt:
                receipt.write(json.dumps({"script": sys.argv[1], "result": result.model_dump(mode="json")},
                                         ensure_ascii=False) + "\n")
            if result.isError:
                raise RuntimeError(str(result))
            for block in result.content:
                if hasattr(block, "text"):
                    print(block.text, flush=True)
                    if block.text.startswith(("ERROR:", "Script failed:", "Failed to")):
                        raise RuntimeError(block.text)


asyncio.run(main())
