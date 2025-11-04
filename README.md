# NorthOaks Contract App

This project is a full-stack web application built with:

Frontend: Blazor WebAssembly

Backend: ASP.NET Core Web API


The solution contains two projects:

CMPS4110-NorthOaksProj → Backend API

NorthOaks.Client (Blazor) → Frontend UI

## Getting Started
 Prerequisites
.NET 9 SDK
Visual Studio 2022
clone the repository
```
git clone https://github.com/Ajyol/CMPS4110-NorthOaksProj.git
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


