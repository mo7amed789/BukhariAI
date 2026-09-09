using System.Text;

namespace BukhariAI.Application.ScratchLearning;

public sealed class ScratchLearningPromptBuilder
{
    public string BuildRoadmapSystemPrompt()
    {
        return """
            أنت "المعلم والمؤسس التعليمي الذكي" في منصة دِراية AI (BukhariAI)، خبير في التدريس التأسيسي المنهجي التفاعلي لكتب التراث والعلوم الشرعية والحديثية (من الصفر إلى الإتقان - Zero to Hero).
            
            رسالتك:
            تبسيط أدق وأعقد المفاهيم ونقل المتعلم من الصفر المطلق إلى الفهم الراسخ ثم التطبيق والإتقان، عبر منهجية تدريجية صارمة لا تفترض معرفة سابقة لدى المتعلم، وتعتمد التشبيهات الحسية الواقعية والأمثلة العملية.

            قواعد المنهجية التأسيسية التراكمية (4 مستويات إجبارية):
            1. **المستوى 1: التأسيس والمدخل البديهي (Level 1: Foundation & Intuition)**:
               - الهدف: كسر حاجز الهيبة، توضيح الصورة الكلية، وشرح الفكرة الجوهرية بتشبيه حسي معاصر دون أي مصطلحات معقدة.
               - الشعار: "افهم المعنى أولاً كما لو كنت تشرحه لشخص عادي في الشارع".
            2. **المستوى 2: البناء والمصطلحات (Level 2: Architecture & Terminology)**:
               - الهدف: إدخال المصطلحات العلمية بدقة وتعريفها وربطها بالأساس التأسيسي الذي فُهم في المستوى الأول.
               - الشعار: "سمّ الأشياء بأسمائها العلمية واعرف وظيفة كل قاعدة".
            3. **المستوى 3: التطبيق ونماذج التراث (Level 3: Hands-on Heritage Application)**:
               - الهدف: تطبيق القواعد على نصوص حقيقية من صحيح البخاري، كتب العلل، أو كتب التفسير، وتحليلها خطوة بخطوة.
               - الشعار: "انزل إلى الميدان وطبق ما تعلمته على نصوص الأئمة".
            4. **المستوى 4: التعميق والإتقان (Level 4: Advanced Nuances & Mastery)**:
               - الهدف: تفكيك الاستثناءات، العلل الخفية، الفروق الدقيقة بين المصطلحات المتقاربة، ومناقشات المحققين.
               - الشعار: "تمييز الدقائق ورسوخ الملكة العلمية".

            يجب أن تكون مخرجاتك بتنسيق JSON صالح ومباشر بدون مقدمات نصية أو علامات markdown خارج كود JSON.
            """;
    }

    public string BuildRoadmapUserPrompt(ScratchRoadmapRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("قم ببناء خارطة طريق تعليمية تفاعلية شاملة (Roadmap) تشرح الموضوع التالي «من الصفر تماماً»:");
        sb.AppendLine($"الموضوع: {request.Topic}");
        sb.AppendLine($"الفئة المستهدفة: {request.TargetAudience}");
        sb.AppendLine($"مستوى العمق: {request.DepthLevel}");
        sb.AppendLine();
        sb.AppendLine("المطلوب: توليد كائن JSON كامل مطابق للهيكل التالي:");
        sb.AppendLine("""
        {
          "topic": "الموضوع المستهدف",
          "title": "عنوان جذاب وخاص بالخارطة التأسيسية",
          "introduction": "مقدمة دافئة ومشجعة تبين للمبتدئ أهمية هذا الموضوع وكيف سيتدرج فيه خطوة بخطوة",
          "targetAudience": "مبتدئ تماماً (من الصفر)",
          "totalMilestones": 8,
          "estimatedTotalTimeMinutes": 45,
          "levels": [
            {
              "levelNumber": 1,
              "levelName": "المستوى الأول: التأسيس والمدخل البديهي",
              "badge": "اللبنة الأولى 🟢",
              "colorTheme": "#10B981",
              "objective": "بناء التصور الكلي المبدئي وتفكيك الحاجز النفسي بدون مصطلحات معقدة",
              "milestones": [
                {
                  "id": "lvl1-m1",
                  "order": 1,
                  "title": "عنوان المحطة الأولى",
                  "shortSummary": "ملخص المحطة في جملة واحدة مكثفة",
                  "icon": "🌱",
                  "keyTerm": "المفهوم الأساسي",
                  "explanation": {
                    "milestoneId": "lvl1-m1",
                    "milestoneTitle": "عنوان المحطة",
                    "levelName": "المستوى الأول",
                    "simpleConcept": "شرح الفكرة الأساسية ببساطة تامة وبلغة عصرية ميسرة",
                    "realWorldAnalogy": "تشبيه واقعي ملموس من الحياة اليومية يقرب المعنى بدقة",
                    "detailedExplanation": "الشرح التعليمي الوافي للمحطة",
                    "steps": [
                      { "stepNumber": 1, "title": "الخطوة الأولى", "explanation": "توضيح الخطوة" },
                      { "stepNumber": 2, "title": "الخطوة الثانية", "explanation": "توضيح الخطوة" }
                    ],
                    "commonPitfalls": ["خطأ شائع يقع فيه المبتدئ 1", "خطأ شائع 2"],
                    "practicalExample": "مثال توضيحي عملي أو أثري مبسط",
                    "quiz": {
                      "question": "سؤال تحقق سريع يقيس الفهم",
                      "options": ["خيار 1", "خيار 2", "خيار 3"],
                      "correctIndex": 0,
                      "explanation": "تعليل الإجابة الصحيحة بأسلوب مشجع",
                      "reinforcementTip": "نصيحة ذهبية لتثبيت هذه النقطة"
                    }
                  }
                }
              ]
            }
          ]
        }
        """);
        sb.AppendLine();
        sb.AppendLine("ملاحظة هامة: يجب أن يحتوي كل مستوى من المستويات الأربعة على محطتين (milestones) على الأقل (أي مجموع 8 محطات أو أكثر)، وكل محطة تحتوي على شرح كامل وكائن quiz.");
        return sb.ToString();
    }

    public string BuildExplainStepPrompt(ExplainScratchStepRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("أنت معلم ومبسط علوم التراث في منصة دِراية AI.");
        sb.AppendLine("قم بكتابة شرح تفصيلي مؤسس من الصفر للمحطة التالية ضمن خارطة طريق:");
        sb.AppendLine($"الموضوع العام: {request.Topic}");
        sb.AppendLine($"المستوى: {request.LevelName} (رقم {request.LevelNumber})");
        sb.AppendLine($"عنوان المحطة: {request.MilestoneTitle}");
        if (!string.IsNullOrWhiteSpace(request.KeyTerm))
        {
            sb.AppendLine($"المصطلح المفتاحي: {request.KeyTerm}");
        }
        sb.AppendLine();
        sb.AppendLine("أخرج كائن JSON صالحاً ومباشراً يحتوي على:");
        sb.AppendLine("""
        {
          "milestoneId": "milestone-id",
          "milestoneTitle": "عنوان المحطة",
          "levelName": "اسم المستوى",
          "simpleConcept": "الفكرة ببساطة شديدة (ELI5)",
          "realWorldAnalogy": "تشبيه حسي معاصر من الحياة الواقعية يرسخ المفهوم",
          "detailedExplanation": "الشرح المنهجي الواضح",
          "steps": [
            { "stepNumber": 1, "title": "عنوان الخطوة", "explanation": "شرح الخطوة" },
            { "stepNumber": 2, "title": "عنوان الخطوة", "explanation": "شرح الخطوة" },
            { "stepNumber": 3, "title": "عنوان الخطوة", "explanation": "شرح الخطوة" }
          ],
          "commonPitfalls": [
            "الوهم أو اللبس الشائع الأول",
            "الوهم أو اللبس الشائع الثاني"
          ],
          "practicalExample": "تطبيق عملي ونموذج حقيقي من صحيح البخاري أو كتب الحديث والتراث",
          "quiz": {
            "question": "سؤال اختبار الفهم السريع",
            "options": ["خيار أ", "خيار ب", "خيار ج"],
            "correctIndex": 0,
            "explanation": "شرح الإجابة ولماذا هي الصحيحة",
            "reinforcementTip": "فائدة إضافية لتثبيت الحفظ والاستيعاب"
          }
        }
        """);
        return sb.ToString();
    }

    public string BuildAskTutorPrompt(AskScratchTutorRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("أنت 'المعلم التأسيسي الذكي' في منصة دِراية AI، متخصص في تبسيط وشرح مواضيع التراث من الصفر التام.");
        sb.AppendLine($"الموضوع الدراسي: {request.Topic}");
        sb.AppendLine($"المحطة الحالية التي يدرسها الطالب: {request.CurrentMilestone}");
        sb.AppendLine($"سؤال الطالب: {request.UserQuestion}");
        sb.AppendLine($"نمط التبسيط المطلوب: {request.SimplicityMode}");
        sb.AppendLine();
        sb.AppendLine("أجب على الطالب بطريقة ودودة ومشجعة جداً بتنسيق JSON التالي:");
        sb.AppendLine("""
        {
          "answer": "الإجابة التبسيطية الشافية بلغة بيضاء ميسرة تخلو من التعقيد",
          "simplifiedAnalogy": "تشبيه عملي ملموس يوضح هذه النقطة المحددة",
          "keyTakeaway": "الخلاصة الذهبية في سطر واحد",
          "followUpSuggestion": "اقتراح ذكي لما يمكن أن يفكر فيه بعد ذلك أو يربطه بما درسه"
        }
        """);
        return sb.ToString();
    }
}
