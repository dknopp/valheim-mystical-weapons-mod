# Blender MCP: setup and lessons

How we let Claude Code drive Blender directly (Thunderfury onwards), so it can build a model part by
part, take screenshots and count triangles. Optional: the Blender scripts in `tools/blender/` also run
without it (see their headers). Who does what is in `building-items.md` ("Custom models: who does what").

## What it is
Blender Lab's official MCP server ([blender.org/lab/mcp-server](https://www.blender.org/lab/mcp-server/),
source at `https://projects.blender.org/lab/blender_mcp`). Two parts that talk over a local socket:
- a **Blender add-on** that runs inside Blender and executes requests (port 9876 by default);
- an **MCP server** that Claude Code launches, which relays Claude's requests to the add-on.

Blender must be open, with the add-on listening, for the tools to work.

## Setup (Windows)
1. **Install uv** (runs the MCP server): `winget install astral-sh.uv`.
2. **Get the server:** `git clone https://projects.blender.org/lab/blender_mcp.git C:\blender_mcp`
   (any folder outside the mod repo works; keep it short and without spaces).
3. **Install the add-on** in Blender (we used Blender Lab's "MCP" add-on, v1.0.3): from the clone's
   `addon/blender_mcp_addon/` folder (Edit > Preferences > Add-ons, install from disk), or from
   Get Extensions if it's listed there. Enable it.
4. **Allow online access:** Preferences > System > Allow Online Access. The add-on won't start without it.
5. **Start the add-on's server** from its preferences panel (or turn on auto-start). It listens on 9876.
6. **Register it with Claude Code at user scope** (not project scope, see Lessons), in a terminal:
   ```
   claude mcp add -s user blender -- "<full path to uv.exe>" --directory C:\blender_mcp\mcp run blender-mcp
   ```
   Use uv's **full path**: winget's uv often isn't on the PATH the Claude desktop app sees. Find it with
   `where.exe uv` in a new terminal (winget puts it under
   `%LOCALAPPDATA%\Microsoft\WinGet\Packages\astral-sh.uv_...\uv.exe`).
7. **Check:** open Blender, start a fresh Claude Code session, and ask it to list the scene's objects.
   `claude mcp list` should show `blender` as connected.

## Using it
- Open Blender (and the item's `.blend`) before starting the session; the tools connect on first use.
- Claude builds from a script, one function per part (`tools/blender/thunderfury_model.py`), runs it
  through the MCP, then shows a screenshot and triangle count after each part.
- Bake and export stay scripted (`tools/blender/make_<item>_texture.py`), so they re-run without the MCP.
- Downloaded reference models go in a hidden collection in the `.blend`, never in the export.

## Lessons (problems we hit)
- **The server vanished mid-session.** After the first turn, the `blender` tools disappeared from the
  session while Blender's add-on was still listening and the config entry was unchanged. The MCP log
  (`%LOCALAPPDATA%\claude-cli-nodejs\Cache\<project>\mcp-logs-blender`) showed Claude Code closing the
  server right after the first turn, reported as "removed from the configuration". Cause: the desktop
  app's between-turn refresh dropped servers registered at **project scope** (`claude mcp add` without
  `-s user`). **Fix:** register it at **user scope** (step 6). Since then it stays connected all session.
- **"uv not found" / server won't start:** the app doesn't see uv on its PATH; use the full path (step 6).
- **Tools fail or time out:** Blender is closed, the add-on isn't started, or Online Access is off
  (steps 4 and 5).
- **Claude can't fix its own config:** in auto mode Claude Code isn't allowed to edit its own MCP
  settings, so you run the `claude mcp` commands yourself.
- If the tools still drop, start a fresh session first; it reconnects.
