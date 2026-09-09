# BukhariAI walkthrough

The adaptive phase adds a backend-owned tutoring decision between persisted learner evidence and Gemini lesson generation. `GET /api/learning/{bookId}/adaptive-state` displays the explainable concept decisions; `POST /api/learning/{bookId}/continue` supplies an intent such as reinforcement or review.

The existing source-bounded lesson and assessment pipeline remains unchanged. Adaptive recommendations identify what to teach next, while Gemini still generates only source-grounded explanations and questions.

The learning-session layer exposes `GET /api/learning/{bookId}/next` and `/dashboard`; both use the same persisted evidence and adaptive decisions to give the frontend one coherent next experience.
