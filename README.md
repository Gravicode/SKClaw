# 🦞 SKClaw

**SKClaw** adalah framework AI Agent berbasis **.NET 9** + **Microsoft Semantic Kernel** — terinspirasi dari OpenClaw, didesain untuk fleksibilitas dan kemudahan deployment.

---

## ✨ Fitur Utama

| Fitur | Keterangan |
|---|---|
| 🤖 **Multi-LLM** | OpenAI, Gemini, Anthropic, Ollama, OpenAI-Compatible API |
| 🧠 **Semantic Memory** | In-Memory, SQLite, Qdrant, Chroma |
| 🔧 **Skills & Tools** | Time, Math, HTTP, File, Search, Translate, Summarize, Email, dll |
| 📡 **MCP Server** | Expose tools ke Claude Desktop, Cursor, dan MCP clients lain |
| 📡 **MCP Client** | Koneksi ke MCP server eksternal |
| 💬 **Channels** | Telegram, Discord, Slack, WhatsApp (Twilio), LINE |
| 🌐 **Web Chat** | Real-time chat via SignalR, dark-themed UI |
| 🔧 **Admin Panel** | Dashboard, tool testing, config viewer |
| 🔗 **Integration API** | REST API OpenAI-compatible + streaming SSE |
| 🖥️ **CLI** | Chat, ask, run, tools, call, serve, status |
| ⚙️ **Single Config** | Semua konfigurasi di `app.config` |
| 🐳 **Docker** | Docker Compose ready dengan Qdrant + Ollama + Nginx |

---

## 🚀 Quick Start

### 1. Clone & Configure

```bash
git clone https://github.com/yourorg/skclaw.git
cd skclaw

# Edit konfigurasi
notepad app.config  # Windows
nano app.config     # Linux/macOS
```

### 2. Set API Key di app.config

```xml
<!-- Minimal: pilih satu provider -->
<add key="LLM:DefaultProvider" value="openai" />
<add key="LLM:OpenAI:ApiKey" value="sk-YOUR_KEY_HERE" />
```

### 3. Jalankan

**Via CLI (chat interaktif):**
```bash
cd src/SKClaw.CLI
dotnet run -- chat
```

**Via Web:**
```bash
cd src/SKClaw.Web
dotnet run
# Buka http://localhost:5000/chat
```

**Via Docker:**
```bash
docker-compose up -d
# Web: http://localhost:5000
# Admin: http://localhost:5000/admin
```

---

## 📦 Struktur Proyek

```
SKClaw/
├── app.config                    ← Semua konfigurasi di sini
├── SKClaw.sln
├── src/
│   ├── SKClaw.Core/              ← Core library
│   │   ├── Agents/               ← SkClawAgent (orchestrator)
│   │   ├── Channels/             ← Telegram, Discord, Slack, WhatsApp
│   │   ├── Configuration/        ← AppConfiguration (baca app.config)
│   │   ├── Connectors/           ← KernelFactory (multi-LLM)
│   │   ├── MCP/                  ← MCP Server & Client
│   │   ├── Memory/               ← SkClawMemory (vector search)
│   │   ├── Models/               ← DTOs dan response models
│   │   └── Skills/               ← Built-in skills/tools
│   ├── SKClaw.CLI/               ← CLI tool (skclaw)
│   ├── SKClaw.Web/               ← Web server (chat + admin + API)
│   │   ├── Controllers/          ← API controllers
│   │   ├── Hubs/                 ← SignalR ChatHub
│   │   └── wwwroot/              ← Frontend (chat UI + admin)
│   ├── SKClaw.MCP/               ← Standalone MCP server
│   └── SKClaw.Plugins/           ← Custom plugins (tambah skill di sini)
├── docker/
│   ├── Dockerfile.web
│   ├── Dockerfile.mcp
│   └── nginx.conf
└── docker-compose.yml
```

---

## ⚙️ Konfigurasi (app.config)

Semua setting ada di satu file `app.config`. Tidak perlu environment variables atau secrets files.

### LLM Providers

```xml
<!-- OpenAI -->
<add key="LLM:DefaultProvider" value="openai" />
<add key="LLM:OpenAI:ApiKey" value="sk-..." />
<add key="LLM:OpenAI:ChatModel" value="gpt-4o" />

<!-- Google Gemini -->
<add key="LLM:DefaultProvider" value="gemini" />
<add key="LLM:Gemini:ApiKey" value="AIza..." />

<!-- Anthropic Claude -->
<add key="LLM:DefaultProvider" value="anthropic" />
<add key="LLM:Anthropic:ApiKey" value="sk-ant-..." />

<!-- Ollama (local) -->
<add key="LLM:DefaultProvider" value="ollama" />
<add key="LLM:Ollama:Endpoint" value="http://localhost:11434" />
<add key="LLM:Ollama:ChatModel" value="llama3.2" />

<!-- OpenAI-Compatible (LM Studio, Groq, Together, etc.) -->
<add key="LLM:DefaultProvider" value="openai-compatible" />
<add key="LLM:OpenAICompatible:Endpoint" value="http://localhost:1234/v1" />
<add key="LLM:OpenAICompatible:ApiKey" value="lm-studio" />
```

### Memory

```xml
<add key="Memory:Provider" value="sqlite" />       <!-- inmemory | sqlite | qdrant | chroma -->
<add key="Memory:Sqlite:ConnectionString" value="Data Source=skclaw_memory.db" />
<add key="Memory:Qdrant:Endpoint" value="http://localhost:6333" />
```

### Channels

```xml
<add key="Channels:Telegram:Enabled" value="true" />
<add key="Channels:Telegram:BotToken" value="1234567890:YOUR_BOT_TOKEN" />

<add key="Channels:Discord:Enabled" value="true" />
<add key="Channels:Discord:BotToken" value="YOUR_DISCORD_TOKEN" />

<add key="Channels:Slack:Enabled" value="true" />
<add key="Channels:Slack:BotToken" value="xoxb-..." />
```

---

## 🖥️ CLI Commands

```bash
skclaw chat              # Chat interaktif dengan AI
skclaw ask "pertanyaan"  # Tanya sekali (non-interaktif)
skclaw run --file p.txt  # Jalankan prompt dari file
skclaw tools             # List semua tools yang tersedia
skclaw call Time.GetCurrentTime --args '{"timezone":"Asia/Jakarta"}'
skclaw mcp list          # List MCP tools
skclaw mcp call skclaw_chat --args '{"message":"Hello"}'
skclaw memory search "pertanyaan sebelumnya"
skclaw serve             # Start web server
skclaw status            # Tampilkan konfigurasi aktif
skclaw version           # Info versi

# In-chat commands:
# /reset    - Reset percakapan
# /history  - Tampilkan riwayat chat
# /save     - Simpan percakapan ke file
# /help     - Tampilkan bantuan
# /exit     - Keluar
```

---

## 🌐 API Endpoints

### Chat API (OpenAI-compatible)

```bash
# Simple chat
POST /api/chat
Authorization: Bearer YOUR_API_KEY
{"message": "Hello SKClaw!"}

# OpenAI-compatible completions
POST /api/chat/completions
{"model": "gpt-4o", "messages": [{"role": "user", "content": "Hello"}], "stream": false}

# Streaming
POST /api/chat/stream
{"message": "Tell me a story"}
# Returns: text/event-stream
```

### MCP Endpoints

```bash
# MCP SSE connection
GET /mcp/sse

# MCP JSON-RPC
POST /mcp/message
{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"Time_GetCurrentTime","arguments":{}}}

# REST MCP
GET /api/mcp/tools
POST /api/mcp/tools/Time_GetCurrentTime
{}
```

### Admin API

```bash
POST /api/admin/login       → JWT token
GET  /api/admin/status      → System status
GET  /api/admin/tools       → Loaded tools
GET  /api/admin/config      → Sanitized config
GET  /api/health            → Health check
```

### Webhooks

```
POST /webhooks/telegram     ← Telegram updates
POST /webhooks/slack        ← Slack Events API
POST /webhooks/discord      ← Discord interactions
POST /webhooks/whatsapp     ← Twilio WhatsApp
```

---

## 🔌 MCP Integration (Claude Desktop / Cursor)

Tambahkan ke `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "skclaw": {
      "url": "http://localhost:5000/mcp/sse",
      "transport": "sse"
    }
  }
}
```

SKClaw akan mengekspos semua tools yang terdaftar ke Claude Desktop.

---

## 🔧 Menambah Custom Skill

Buat class di `src/SKClaw.Plugins/Skills/`:

```csharp
using System.ComponentModel;
using Microsoft.SemanticKernel;

public class MySkill
{
    [KernelFunction, Description("Deskripsi yang jelas untuk AI")]
    public async Task<string> DoSomethingAsync(
        [Description("Parameter pertama")] string input,
        [Description("Parameter kedua")] int count = 5)
    {
        // Implementasi Anda
        return $"Result: {input} x {count}";
    }
}
```

Daftarkan di startup:
```csharp
kernel.ImportPluginFromObject(new MySkill(), "MySkill");
```

---

## 🐳 Docker Deployment

```bash
# Development
docker-compose up -d

# Dengan Ollama (GPU)
docker-compose --profile ollama up -d

# Dengan Nginx reverse proxy
docker-compose --profile nginx up -d

# Lihat logs
docker-compose logs -f skclaw-web
```

---

## 📋 Built-in Skills

| Skill | Functions |
|---|---|
| **Time** | GetCurrentTime, DateDiff, AddTime |
| **Math** | Calculate, ConvertUnit |
| **Http** | Get, PostJson |
| **File** | Read, Write, List, Append |
| **Search** | SearchWeb (Bing/Google/DuckDuckGo) |
| **Summarize** | Summarize, ExtractKeyInfo |
| **Translate** | Translate, DetectLanguage |
| **Email** | SendEmail |
| **Weather*** | GetWeather |
| **News*** | GetHeadlines |
| **Currency*** | ConvertCurrency, GetRates |
| **CodeReview*** | ReviewCode, GenerateTests |

*= Di SKClaw.Plugins (contoh custom skill)

---

## 📝 License

MIT License — bebas digunakan, dimodifikasi, dan didistribusikan.

---

*Built with ❤️ using Microsoft Semantic Kernel*
