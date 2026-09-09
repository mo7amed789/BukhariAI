# Adaptive Learning Engine

`StudentConceptMastery` remains the single concept-evidence record. Its `MasteryScore` is evidence accumulated by the mastery engine; the adaptive engine derives a separate, read-time confidence score from that evidence.

## Learning states and actions

Unknown → Introduce. Introduced → Explain. Familiar → AskQuestion, or Reinforce when weak. Understood → VerifyMastery, or Review when retrieval is overdue. Mastered → NoAction while recent, otherwise Review.

Understanding and mastery are never assigned by Gemini. The backend policy requires assessment evidence; mastery still requires score ≥ .85, three correct answers, and three distinct lesson contexts.

## Confidence

Confidence is deterministic: 45% accumulated evidence score, 25% answer accuracy, 15% distinct successful contexts, 10% exposure, and 5% assessment recency, minus a failure penalty of up to 25%. It is clamped to 0–1. This makes a recent, consistent cross-context learner more confident than a frequently exposed learner with no demonstrated understanding.

## Reviews and prompting

Weak or overdue concepts create stored recommendations with a reason code, current level, recommended action, question type, source lesson, and source pages. Lesson prompts carry the backend learning intent and decisions alongside the existing source-first educational memory. Gemini uses that direction only to teach; it cannot alter the learner record.

## Example journey

Seeing `الدباغ` once produces Introduced → Explain. Repeated exposure without proof becomes Familiar → AskQuestion. Weak answers result in Reinforce with a targeted understanding question. Two solid answers result in Understood → VerifyMastery. Three successful distinct lesson contexts at the existing mastery threshold result in Mastered → NoAction until review is due.
