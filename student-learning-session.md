# Student Learning Session Layer

`LearningSessionService` is the frontend-facing tutor façade. It reads existing lesson progress, assessment evidence, review recommendations, source pages, and `AdaptiveLearningService` decisions; it does not recalculate mastery.

## Next-action priority

1. Continue an unfinished or review-needed lesson.
2. Return a pending assessment for a lesson that has been read.
3. Return a due review recommendation.
4. Reinforce the weakest concept.
5. Verify mastery for an understood concept.
6. Start a new book at pages 1–2, or advance only to known uncompleted source pages.
7. Mark the known source set complete.

Every response includes a deterministic reason code, learning intent, relevant source pages, concepts, progress, and optional assessment/review data. The frontend renders this decision and never calculates confidence or priority.

Lesson generation alone does not complete a lesson. Existing progress requires learner activity and assessment evidence; weak evidence leads to `NeedsReview`.
