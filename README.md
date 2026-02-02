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
 Prerequisites
.NET 9 SDK
Visual Studio 2022
clone the repository
```
git clone https://github.com/Subeen9/NorthOaks-AI-Contract-App.git
```

 ###  Run the Backend (Web API)

1. Open the solution in **Visual Studio**.  
2. Right-click `CMPS4110-NorthOaksProj` → **Set as Startup Project**.  
3. Run with **Ctrl + F5**.  

By default, the frontend also runs on the same port:  
- API Base URL → [https://localhost:7161](https://localhost:7161)  
- Swagger UI → [https://localhost:7161/swagger/index.html](https://localhost:7161/swagger/index.html)  

## AI Pipeline - We are using Ollama for Embedding Generation and  Semantic Search
 - Download ollama [Download Here](https://ollama.com/download/windows)

 ```bash
 ollama serve # To start ollama server
 ```
 - Download the model[nomic-embed-text:latest] 
 ```bash
 ollama pull nomic-embed-text:latest # For embedding generation
 ```
 ```bash
 ollama pull llama3.2:latest # For Answer Generation
```
## Vector Database - We are using Qdrant for Vector Database 
**You need to install docker Desktop to run the Qdrant**
Download Docker Desktop [Download Here](https://docs.docker.com/desktop/setup/install/windows-install/)

In the command line run the following command to run the Qdrant 
```bash
docker run -p 6333:6333 -p 6334:6334 -v %cd%/qdrant_storage:/qdrant/storage qdrant/qdrant
```
You would be able to see Qdrant's instance running on the Docker Desktop.
To see the collection in Qdrant,
```bash
http://localhost:6333/dashboard#/collections # Once you have generated embeddings the chunks should be here
```
## Prereq for pdfjs
Download the pdfjs
```bash
https://mozilla.github.io/pdf.js/getting_started/#download
```
**Extract the Zip file and place pdfjs folder with both build and web inside NorthOaks.Client/wwwroot**

## Common TroubleShoot Methods
If you get this Error, while trying to chat
 **In Console**  Generation failed after 4025ms
      System.Net.Http.HttpRequestException: Response status code does not indicate success: 500 (Internal Server Error).
         at System.Net.Http.HttpResponseMessage.EnsureSuccessStatusCode()

**OR IN Chat Interface**
An unexpected error occurred while processing your message.

The primary Reason it might happen is Ollama is not working properly
Run Ollama by running 
```bash
ollama serve
``` 
If it says **Error: listen tcp 127.0.0.1:11434: bind: Only one usage of each socket address (protocol/network address/port) is normally permitted.**
Ollama is running fine so problem is another thing.

Another problem might be low memory in your PC. 
To verify Run:
```bash
model requires more system memory than is available
```
Go to the Task Manager to End some task and free some memory required to run the model.

In command line you get this Error:
 CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing.DocumentProcessingService[0]
      Error embedding chunk 3 for contract 1042
      Grpc.Core.RpcException: Status(StatusCode="Unavailable", Detail="Error connecting to subchannel.", DebugException="System.Net.Sockets.SocketException: No connection could be made because the target machine actively refused it.")
       ---> System.Net.Sockets.SocketException (10061): No connection could be made because the target machine actively refused

Primary reason this occurs is you forgot to run the Qdrant Instance in the Docker Desktop. If Qdrant Instance is running, try by stopping or creating new instance again.


