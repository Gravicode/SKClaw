#!/usr/bin/env bash
# setup.sh - SKClaw Setup Script
# Usage: chmod +x setup.sh && ./setup.sh

set -e
CYAN='\033[0;36m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; RED='\033[0;31m'; NC='\033[0m'

echo -e "${CYAN}🦞 SKClaw Setup${NC}"
echo -e "${CYAN}===============${NC}"

# Check .NET 9
if ! command -v dotnet &>/dev/null; then
  echo -e "${RED}❌ dotnet not found. Install from: https://dot.net${NC}"
  exit 1
fi

DOTNET_VER=$(dotnet --version)
if [[ ! "$DOTNET_VER" == 9.* ]]; then
  echo -e "${YELLOW}⚠️  .NET $DOTNET_VER found. .NET 9 recommended.${NC}"
else
  echo -e "${GREEN}✅ .NET $DOTNET_VER${NC}"
fi

# Copy app.config to projects
PROJECTS=("src/SKClaw.CLI" "src/SKClaw.Web" "src/SKClaw.MCP")
for proj in "${PROJECTS[@]}"; do
  dest="$proj/app.config"
  if [ ! -f "$dest" ]; then
    cp app.config "$dest"
    echo -e "${GREEN}✅ Copied app.config → $dest${NC}"
  else
    echo -e "${YELLOW}⚠️  $dest exists (skipped)${NC}"
  fi
done

# Restore & build
echo -e "\n${CYAN}Restoring packages...${NC}"
dotnet restore SKClaw.sln

echo -e "\n${CYAN}Building...${NC}"
dotnet build SKClaw.sln -c Debug --no-restore

echo -e "\n${GREEN}✅ Setup complete!${NC}"
echo ""
echo "Next steps:"
echo "  1. Edit app.config and set your API keys"
echo "  2. CLI:   cd src/SKClaw.CLI && dotnet run -- chat"
echo "  3. Web:   cd src/SKClaw.Web && dotnet run"
echo "  4. Docker: docker-compose up -d"
echo ""
echo -e "${CYAN}Web Chat:  http://localhost:5000/chat${NC}"
echo -e "${CYAN}Admin:     http://localhost:5000/admin${NC}"
echo -e "${CYAN}API:       http://localhost:5000/api${NC}"
echo -e "${CYAN}MCP SSE:   http://localhost:5000/mcp/sse${NC}"
