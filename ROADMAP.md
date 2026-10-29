# Unity MCP Hub Roadmap

## Implemented

- Detect multiple running Unity Editor processes.
- Temporarily inject and cleanly restore one UPM package dependency.
- Load the official Coplay MCP for Unity package instead of duplicating Unity commands.
- Download and validate one shared local Coplay package cache, add Hub auto-connect integration, and inject one local `file:` dependency.
- Start a version-matched official Coplay MCP server through `uvx`.
- Expose a stable Hub MCP endpoint through a transparent HTTP reverse proxy.
- Display Coplay connection state for discovered Unity projects.
- Delegate tool discovery, tool execution, and MCP-session instance selection to Coplay.
- Restore manifest and lock-file state exactly, including recovery after an unclean shutdown.
- Build a self-contained Windows x64 offline release with the .NET runtime, Python runtime, Coplay server, and Unity package.

## Next

- Resolve duplicate project-folder names by matching Coplay instances with a stable project identifier when available.
- Track in-flight proxy requests during shutdown and add protocol tests for SSE, session headers, and cancellation.
- Verify the pinned Coplay package archive against a published checksum before caching it.
- Add a connection diagnostics view and copyable MCP client configuration snippets.
- Make public and upstream ports configurable from the UI and command line.
- Add automated proxy protocol tests and a Unity package compilation fixture.
- Add an installer and optional one-click MCP client configuration.

For a prioritized technical review, see [Docs/PROJECT_REVIEW.zh-CN.md](Docs/PROJECT_REVIEW.zh-CN.md).
