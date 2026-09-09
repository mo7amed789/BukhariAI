# BukhariAI 📚🧠

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" />
  <img src="https://img.shields.io/badge/C%23-13.0-239120?style=for-the-badge&logo=csharp&logoColor=white" />
  <img src="https://img.shields.io/badge/Architecture-Clean%20Architecture-007ACC?style=for-the-badge" />
  <img src="https://img.shields.io/badge/AI%20%2F%20OCR-Tesseract-blue?style=for-the-badge" />
  <img src="https://img.shields.io/badge/CQRS-MediatR-orange?style=for-the-badge" />
</p>

---

## 📖 Overview

**BukhariAI** is an intelligent AI-powered educational and research platform built on **.NET 10** with **Clean Architecture**. It provides deep semantic search, Optical Character Recognition (OCR) for classical Arabic manuscripts, and adaptive learning algorithms to guide students and researchers through Sahih al-Bukhari.

---

- **🏛️ Clean Architecture & DDD:** Strict separation of concerns across Domain, Application, Infrastructure, and Presentation layers.
- **🎓 Zero-to-Hero Foundation Engine (شرح من الصفر):** Interactive 4-level progressive learning roadmaps with real-world analogies, step-by-step breakdown, heritage examples, and checkpoint quizzes.
- **🕋 Quran Tadabbur & Memorization:** Comprehensive thematic analysis for all 114 Surahs, inter-ayah Munasabat, Waqf & Ibtida rules, and interactive recitation grading.
- **🔍 Optical Character Recognition (OCR):** Embedded Tesseract integration optimized for classical Arabic text and manuscript digitization.
- **🧠 Adaptive Learning Engine (SM-2):** Personalized learning sessions and spaced repetition for students of knowledge.
- **⚡ High-Performance CQRS & Vision AI:** Clean pipelines with FluentValidation, MediatR, and multi-model AI orchestration (Gemini, Claude, GPT).
- **🧪 Comprehensive Unit & Integration Testing:** Full test suite with 73+ passing unit tests covering domain entities, prompts, and learning engines.

---

## 🏗️ Architecture

```text
BukhariAI/
├── src/
│   ├── BukhariAI.Domain/          # Core entities, value objects, domain events
│   ├── BukhariAI.Application/     # Use cases, CQRS commands/queries, interfaces
│   ├── BukhariAI.Infrastructure/  # OCR engines, repositories, database persistence
│   └── BukhariAI.Api/             # RESTful API controllers and endpoints
└── tests/                         # Unit, integration, and benchmark tests
```

---

## 🚀 Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Run Application
```bash
dotnet restore
dotnet build
dotnet run --project src/BukhariAI.Api
```

### Run Tests
```bash
dotnet test
```

---

## 📜 License
This project is licensed under the MIT License.
