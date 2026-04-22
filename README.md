# TuneFinder AI

A full-stack AI Music Discovery Assistant for class demos.

This project demonstrates:
- LLM chat responses
- RAG (retrieval-augmented generation) from a curated music knowledge base
- Function calling with music tools

## Live Demo

- Open the app: `https://tunefinderai.netlify.app`
- No local setup is required for normal use.
- Type a prompt in chat and the deployed backend handles responses.

## Tech Stack

- Frontend: `HTML + Bootstrap 5 + vanilla JS`
- Backend: `ASP.NET Core Web API (.NET 8, C#)`
- Database: `MySQL` (raw SQL with `MySqlConnector`, no ORM)
- AI: OpenAI-compatible API (`gpt-4o-mini` + optional embeddings)

## Project Structure

```text
.
├─ backend/
│  └─ TuneFinder.Api/
│     ├─ Controllers/
│     ├─ Contracts/
│     ├─ Models/Llm/
│     ├─ Options/
│     ├─ SeedData/
│     ├─ Services/
│     │  ├─ Interfaces/
│     │  ├─ Llm/
│     │  ├─ Rag/
│     │  └─ Tools/
│     ├─ Utils/
│     ├─ Program.cs
│     └─ TuneFinder.Api.csproj
├─ frontend/
│  ├─ index.html
│  ├─ app.js
│  └─ styles.css
├─ .env.example
└─ README.md
```

## Run Instructions

- Open the app in your browser:
  - `https://tunefinderai.netlify.app`

## Database Schema

The app auto-creates these tables on startup:
- `artists`
- `songs`
- `albums`
- `documents`
- `document_chunks`
- `chat_messages`

Seed data includes:
- 25 artists
- 40 songs
- 12 albums
- 20 knowledge documents
- playlist theme seed file

## Backend Setup

1) Install .NET 8 SDK

2) Configure environment variables  
Use `.env.example` as reference.

Minimum required:
- `ConnectionStrings__Default` (or `JAWSDB_URL`)
- `Ai__ApiKey`
- `Ai__BaseUrl`
- `Ai__ChatModel`

3) Run backend

```bash
dotnet run --project backend/TuneFinder.Api/TuneFinder.Api.csproj
```

Default local API URL is:
- `http://localhost:5001` (set in `appsettings.Development.json`)

Swagger is enabled in development:
- `/swagger`

## Frontend Setup

Open `frontend/index.html` in a browser, or serve with any static file host.

By default, frontend calls:
- `http://localhost:5001`

To change backend URL in production, set:
- `window.TUNEFINDER_API_BASE_URL` before loading `app.js`

Example:
```html
<script>window.TUNEFINDER_API_BASE_URL = "https://your-backend-url.com";</script>
<script src="./app.js"></script>
```

## API Endpoint

### `POST /api/chat`

Request:
```json
{
  "sessionId": "demo-session-1",
  "message": "Make me a high energy workout playlist."
}
```

Response:
```json
{
  "sessionId": "demo-session-1",
  "response": "...assistant message...",
  "usedRag": true,
  "toolsUsed": ["createPlaylist"],
  "retrievedContext": ["...chunk 1...", "...chunk 2..."]
}
```

## Function Calling Tools

Implemented tools:
- `recommendSongsByMood(mood)`
- `findSimilarArtists(artistName)`
- `createPlaylist(theme)`
- `searchArtistInfo(artistName)`

Each tool runs against local MySQL seed data (no external music API dependency).

## RAG Implementation

Simple and explainable pipeline:
1) Seed curated docs into `documents`
2) Auto-generate additional docs from `artists`, `songs`, `albums`, and genre stats
3) Chunk all docs into `document_chunks`
4) Generate and store embeddings for chunks in `document_chunks.embedding`
5) Generate query embedding at runtime and rank chunks by cosine similarity
6) Fallback to keyword retrieval if embeddings are unavailable
7) Inject top chunks into system context
8) Generate final answer with LLM

Embedding details:
- embeddings come from the configured OpenAI-compatible provider
- vectors are stored as JSON in MySQL (`LONGTEXT`)
- retrieval uses in-app cosine similarity ranking
- keyword fallback keeps RAG working during provider quota/rate-limit errors

## Heroku MySQL (JawsDB) Notes

The backend supports:
- `ConnectionStrings__Default` directly, or
- `JAWSDB_URL` / `DATABASE_URL` parsing

If using Heroku MySQL add-on, set env vars in backend host:
- `JAWSDB_URL` (from Heroku config)
- `Ai__ApiKey`
- `Ai__BaseUrl`
- `Ai__ChatModel`
- `Ai__EmbeddingModel`

The app enforces SSL mode for URL-based DB connections.

## Deployment Plan

### Frontend (static)
Deploy `frontend/` to:
- Netlify, Vercel, GitHub Pages, or Cloudflare Pages

### Backend (web service)
Deploy `backend/TuneFinder.Api` to:
- Render / Railway / Heroku

### Database
- Heroku MySQL add-on (JawsDB)

## Demo Prompts

- "Recommend 8 chill songs for a rainy night."
- "Find artists similar to SZA and explain why."
- "Create a workout playlist with high energy songs."
- "Give me artist info for Frank Ocean."
- "What makes indie rock different from alternative pop?"

## Notes for Class Presentation

- LLM usage: OpenAI-compatible chat completion endpoint
- Function calling: model chooses and triggers tools
- RAG: retrieved local knowledge chunks are injected into prompt
- Data persistence: chat history saved in MySQL by `sessionId`
