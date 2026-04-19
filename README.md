# 🌟 LuminaVault

**LuminaVault** is an AI‑powered photo and video management system built with **.NET Aspire**.  
It offers fast search, automatic tagging, GPS‑based organization, manual categories, and high‑performance processing for large media libraries.

---

## 🚀 Features

- 🔍 **Search** — Full‑text, tag, and similarity search using vector embeddings  
- 🤖 **AI Tagging** — Automatic classification and OCR via local or cloud models  
- 🗂️ **Manual Categories** — Create, edit, and assign custom categories  
- 📍 **GPS Integration** — Extract and visualize location data on maps  
- 🖼️ **Media Processing** — Thumbnails, video previews, duplicate detection  
- ⚙️ **Scalable Architecture** — Handles 22,000+ photos and 100 GB of data  

---

## 🏗️ Architecture Overview

LuminaVault is composed of modular services orchestrated by **.NET Aspire**:

| Service | Description |
|----------|--------------|
| **MediaImporterService** | Scans directories, extracts metadata, generates hashes |
| **MetadataService** | Stores EXIF, GPS, tags, and AI results |
| **SearchService** | Full‑text and vector search |
| **AIServices** | Classification, embeddings, OCR |
| **ThumbnailService** | Image and video preview generation |
| **APIGateway** | Unified API layer |
| **WebUI** | Modern interface (Blazor or React) |

**Storage:** PostgreSQL + pgvector or Qdrant  
**Media Storage:** MinIO or local filesystem  

---

## 🧭 Roadmap

- Face recognition  
- Advanced duplicate finder  
- Timeline view  
- Cloud sync  
- Mobile companion app  

---

## 🧩 Tech Stack

- **.NET Aspire**  
- **C# / Blazor**  
- **PostgreSQL / pgvector**  
- **MinIO / S3‑compatible storage**  
- **Ollama / Qwen‑VL / CLIP** for AI tagging  

---

## 📜 License

This project is licensed under the **MIT License** — allowing commercial use and future closed‑source editions.

---

## 💡 Project Setup

```bash
git clone https://github.com/KaiUweSchmidt/LuminaVault.git
cd LuminaVault
dotnet restore
dotnet run
