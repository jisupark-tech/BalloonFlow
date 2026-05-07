@echo off
cd /d C:\projects\balloonflow\projecthub-mcp\server
set PROJECTHUB_MCP_TOKEN=ce9c9ea6fee8473f8d7686fb974105033e75c40855e27568028b9fe00037f044
node index.mjs > mcp.log 2> mcp.err
