# NorthOaks Contract App

**North Oaks Contract AI** is a specialized Retrieval-Augmented Generation (RAG) platform built for the staff at North Oaks Health System. It transforms static legal documents into interactive, chat-ready assets, allowing users to query complex contracts through a secure, context-aware AI interface.


##  Key Features

* **Intelligent Document Chat:** Upload PDFs and interact with a Llama 3.2-powered AI that understands the specific nuances of your document.
* **Dual-Engine Extraction:** Utilizes **Tesseract OCR** for image-heavy scans and **PDFPig** for digital text to ensure high-fidelity data extraction.
* **Side-by-Side Viewing:** Interactive UI allows users to view the original PDF alongside the AI chat for immediate verification.
* **Clause Comparison:** Compare different contracts to identify variations in legal clauses.
* **Source Citation:** AI responses include JSON-referenced source highlighting to point users exactly where in the document the information was found.
* **Real-Time Processing:** Live upload tracking and status updates powered by **SignalR**.
* **Security & Privacy:** Support for Public/Private contract designations to manage sensitive information.

## Tech Stack

* **Frontend/Backend:** Blazor (ASP.NET Core)
* **AI Inference:** Ollama (Llama 3.2)
* **Embeddings:** Nomic Embed Text
* **Vector Database:** Qdrant (using Cosine Similarity)
* **Document Processing:** Tesseract OCR & PDFPig
* **Real-Time Updates:** SignalR
* **Database:** Microsoft SQL Server
*  **Containerization:** Docker Desktop


The solution contains two projects:

CMPS4110-NorthOaksProj → Backend API

NorthOaks.Client (Blazor) → Frontend UI

## Getting Started
1.  Prerequisites
.NET 9 SDK
Visual Studio 2022
clone the repository
```
git clone https://github.com/Ajyol/CMPS4110-NorthOaksProj.git
```

2. ### 2. Run the Backend (Web API)

1. Open the solution in **Visual Studio**.  
2. Right-click `CMPS4110-NorthOaksProj` → **Set as Startup Project**.  
3. Run with **Ctrl + F5**.  

By default, the frontend also runs on the same port:  
- API Base URL → [https://localhost:7161](https://localhost:7161)  
- Swagger UI → [https://localhost:7161/swagger/index.html](https://localhost:7161/swagger/index.html)  

