"""Send reproducible drawing scripts to the locally installed Aseprite MCP server."""
import asyncio
import json
import os
from pathlib import Path
import sys

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


async def main():
    server = Path(os.environ.get("MEMORY_ASEPRITE_MCP", str(Path.home() / ".codex/aseprite-mcp")))
    env = dict(os.environ)
    env["PYTHONPATH"] = str(server)
    env["ASEPRITE_PATH"] = os.environ.get("ASEPRITE_PATH", "H:/SteamLibrary/steamapps/common/Aseprite/Aseprite.exe")
    params = StdioServerParameters(command=sys.executable, args=["-m", "aseprite_mcp"], env=env, cwd=str(server))
    async with stdio_client(params) as (reader, writer):
        async with ClientSession(reader, writer) as session:
            await session.initialize()
            if len(sys.argv) < 2:
                result = await session.list_tools()
                print(json.dumps([{ "name": t.name, "schema": t.inputSchema } for t in result.tools if t.name in ("run_lua_script", "get_sprite_info", "get_color_stats", "export_sprite")], ensure_ascii=False, indent=2))
                return
            script = Path(sys.argv[1]).read_text(encoding="utf-8-sig")
            wrapped = "local ok, failure = xpcall(function()\n" + script + "\nend, debug.traceback)\nif not ok then print('ERROR:' .. tostring(failure)) end"
            result = await session.call_tool("run_lua_script", {"script": wrapped})
            for content in result.content:
                if hasattr(content, "text"):
                    print(content.text)
                    if "ERROR:" in content.text or content.text.startswith("Script failed:"):
                        raise RuntimeError("Aseprite drawing failed; see tool output")
            if result.isError:
                raise RuntimeError("Aseprite MCP failed")


if __name__ == "__main__":
    asyncio.run(main())
