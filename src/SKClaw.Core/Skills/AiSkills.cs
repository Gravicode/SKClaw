using System.ComponentModel;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace SKClaw.Core.Skills;

/// <summary>
/// SummarizeSkill — AI-powered summarization, extraction, and content analysis.
/// </summary>
public class SummarizeSkill
{
    private readonly Kernel _kernel;
    public SummarizeSkill(Kernel kernel) => _kernel = kernel;

    [KernelFunction, Description("Summarize a long text into a concise paragraph")]
    public async Task<string> SummarizeAsync(
        [Description("Text to summarize")] string text,
        [Description("Max summary length in words")] int maxWords = 150)
    {
        return await Prompt($"Summarize the following text in at most {maxWords} words. Be clear and concise:\n\n{text}");
    }

    [KernelFunction, Description("Summarize text as bullet-point key takeaways")]
    public async Task<string> BulletPointsAsync(
        [Description("Text to summarize")] string text,
        [Description("Maximum number of bullet points")] int maxPoints = 7)
    {
        return await Prompt($"Extract the {maxPoints} most important key points from this text as a concise bullet list (one per line, starting with •):\n\n{text}");
    }

    [KernelFunction, Description("Create an executive summary suitable for a business audience")]
    public async Task<string> ExecutiveSummaryAsync(
        [Description("Text to summarize")] string text,
        [Description("Target audience description")] string audience = "executives")
    {
        return await Prompt($"Write a professional executive summary of this content for {audience}. Include: Purpose, Key Findings, Implications, and Recommended Actions.\n\n{text}");
    }

    [KernelFunction, Description("Extract structured information: entities, facts, dates, and actions from text")]
    public async Task<string> ExtractInfoAsync(
        [Description("Text to extract information from")] string text,
        [Description("What to extract: entities, facts, dates, actions, topics, sentiment, or all")] string extractType = "all")
    {
        var prompt = extractType.ToLower() switch
        {
            "entities"  => $"Extract all named entities (people, organisations, locations, products) from this text. Return as JSON array.",
            "facts"     => $"Extract concrete factual statements from this text. Return as numbered list.",
            "dates"     => $"Extract all dates, times, and temporal references from this text. Return as a list.",
            "actions"   => $"Extract all action items, tasks, or commitments from this text. Return as a checklist.",
            "topics"    => $"Identify the main topics and sub-topics in this text. Return as a topic hierarchy.",
            "sentiment" => $"Analyse the sentiment of this text. Return: overall sentiment (positive/negative/neutral/mixed), confidence (0-1), key sentiment indicators.",
            _           => $"Analyse this text and extract: 1) Named Entities (people, orgs, places) 2) Key Facts 3) Dates & Times 4) Action Items 5) Main Topics 6) Overall Sentiment",
        };
        return await Prompt($"{prompt}\n\nText:\n{text}");
    }

    [KernelFunction, Description("Generate questions about a topic or text (for study, interview, or QA)")]
    public async Task<string> GenerateQuestionsAsync(
        [Description("Topic or text to generate questions for")] string content,
        [Description("Number of questions to generate")] int count = 5,
        [Description("Type: comprehension, critical, interview, trivia, socratic")] string type = "comprehension")
    {
        return await Prompt($"Generate {count} {type} questions about the following content. Number each question.\n\n{content}");
    }

    [KernelFunction, Description("Answer a question based on provided context (RAG-style)")]
    public async Task<string> AnswerFromContextAsync(
        [Description("Context/document to use for answering")] string context,
        [Description("Question to answer")] string question)
    {
        return await Prompt($"Based ONLY on the following context, answer the question. If the answer is not in the context, say so.\n\nContext:\n{context}\n\nQuestion: {question}");
    }

    [KernelFunction, Description("Classify text into predefined categories")]
    public async Task<string> ClassifyAsync(
        [Description("Text to classify")] string text,
        [Description("Comma-separated list of categories")] string categories,
        [Description("Allow multiple categories? true/false")] bool multiLabel = false)
    {
        var mode = multiLabel ? "all applicable" : "the single best";
        return await Prompt($"Classify the following text into {mode} of these categories: [{categories}]\nReturn only the category name(s), no explanation.\n\nText: {text}");
    }

    private async Task<string> Prompt(string text)
    {
        var result = await _kernel.InvokePromptAsync(text);
        return result.GetValue<string>() ?? "";
    }
}

/// <summary>
/// TranslateSkill — AI-powered translation and language utilities.
/// </summary>
public class TranslateSkill
{
    private readonly Kernel _kernel;
    public TranslateSkill(Kernel kernel) => _kernel = kernel;

    [KernelFunction, Description("Translate text from one language to another")]
    public async Task<string> TranslateAsync(
        [Description("Text to translate")] string text,
        [Description("Target language, e.g. Indonesian, English, French, Spanish, Japanese")] string targetLanguage,
        [Description("Source language (leave empty to auto-detect)")] string sourceLanguage = "",
        [Description("Translation tone: formal, informal, technical, literary")] string tone = "formal")
    {
        var from = string.IsNullOrEmpty(sourceLanguage) ? "auto-detected language" : sourceLanguage;
        var result = await _kernel.InvokePromptAsync(
            $"Translate the following text from {from} to {targetLanguage} using a {tone} tone. Return only the translation, no explanation.\n\n{text}");
        return result.GetValue<string>() ?? "";
    }

    [KernelFunction, Description("Detect the language of a text and provide confidence")]
    public async Task<string> DetectLanguageAsync([Description("Text to detect")] string text)
    {
        return await _kernel.InvokePromptAsync(
            $"Detect the language of this text. Return format: 'Language: <name> (code: <ISO-639-1>)'\n\n{text}").ContinueWith(t => t.Result.GetValue<string>() ?? "");
    }

    [KernelFunction, Description("Translate text to multiple target languages at once")]
    public async Task<string> TranslateMultipleAsync(
        [Description("Text to translate")] string text,
        [Description("Comma-separated list of target languages")] string targetLanguages)
    {
        var langs = targetLanguages.Split(',').Select(l => l.Trim()).ToList();
        var sb = new StringBuilder();
        foreach (var lang in langs)
        {
            var result = await _kernel.InvokePromptAsync(
                $"Translate to {lang}. Return only the translation:\n\n{text}");
            sb.AppendLine($"[{lang}]: {result.GetValue<string>()}");
        }
        return sb.ToString().Trim();
    }

    [KernelFunction, Description("Improve or paraphrase text while keeping the same meaning")]
    public async Task<string> ParaphraseAsync(
        [Description("Text to paraphrase")] string text,
        [Description("Style: simpler, professional, casual, creative, concise, expanded")] string style = "professional")
    {
        return await _kernel.InvokePromptAsync(
            $"Paraphrase the following text in a {style} style. Keep the same meaning but change the wording:\n\n{text}").ContinueWith(t => t.Result.GetValue<string>() ?? "");
    }

    [KernelFunction, Description("Check grammar and spelling in a text")]
    public async Task<string> GrammarCheckAsync([Description("Text to check")] string text)
    {
        return await _kernel.InvokePromptAsync(
            $"Check the grammar and spelling of this text. List all errors with corrections, then provide the corrected version:\n\n{text}").ContinueWith(t => t.Result.GetValue<string>() ?? "");
    }

    [KernelFunction, Description("Adjust the reading level or formality of text")]
    public async Task<string> AdjustReadingLevelAsync(
        [Description("Text to adjust")] string text,
        [Description("Target level: elementary, middle_school, high_school, college, expert, ELI5")] string level)
    {
        return await _kernel.InvokePromptAsync(
            $"Rewrite this text at a {level} reading level. Adjust vocabulary and sentence structure accordingly:\n\n{text}").ContinueWith(t => t.Result.GetValue<string>() ?? "");
    }
}

/// <summary>
/// ReasoningSkill — AI-powered reasoning, planning, decision support,
/// chain-of-thought, debate, and structured thinking tools.
/// </summary>
public class ReasoningSkill
{
    private readonly Kernel _kernel;
    public ReasoningSkill(Kernel kernel) => _kernel = kernel;

    [KernelFunction, Description("Break down a complex problem using step-by-step chain-of-thought reasoning")]
    public async Task<string> ChainOfThoughtAsync(
        [Description("Problem or question to reason through")] string problem)
    {
        return await Prompt($"""
            Solve this problem using careful step-by-step reasoning. 
            Show your work explicitly at each step before giving the final answer.
            
            Problem: {problem}
            
            Let me think through this step-by-step:
            """);
    }

    [KernelFunction, Description("Analyse pros and cons of a decision or option")]
    public async Task<string> ProsConsAsync(
        [Description("Decision, option, or topic to analyse")] string topic,
        [Description("Context or constraints to consider")] string context = "")
    {
        return await Prompt($"""
            Provide a balanced pros and cons analysis for: {topic}
            {(string.IsNullOrEmpty(context) ? "" : $"Context: {context}")}
            
            Format as:
            PROS:
            1. ...
            
            CONS:
            1. ...
            
            VERDICT: (brief recommendation)
            """);
    }

    [KernelFunction, Description("Apply SWOT analysis (Strengths, Weaknesses, Opportunities, Threats)")]
    public async Task<string> SwotAnalysisAsync(
        [Description("Subject to analyse (person, company, product, strategy)")] string subject)
    {
        return await Prompt($"Conduct a thorough SWOT analysis for: {subject}\n\nFormat each section clearly with 4-6 points each.");
    }

    [KernelFunction, Description("Generate a structured plan with steps, timeline, and resources")]
    public async Task<string> CreatePlanAsync(
        [Description("Goal or objective")] string goal,
        [Description("Available resources or constraints")] string constraints = "",
        [Description("Timeframe for the plan")] string timeframe = "")
    {
        return await Prompt($"""
            Create a detailed, actionable plan to achieve this goal:
            Goal: {goal}
            {(string.IsNullOrEmpty(constraints) ? "" : $"Constraints: {constraints}")}
            {(string.IsNullOrEmpty(timeframe) ? "" : $"Timeframe: {timeframe}")}
            
            Include: Phase breakdown, specific actions per phase, success metrics, potential risks.
            """);
    }

    [KernelFunction, Description("Generate creative ideas using brainstorming techniques (SCAMPER, mind mapping, etc.)")]
    public async Task<string> BrainstormAsync(
        [Description("Topic or problem to brainstorm")] string topic,
        [Description("Technique: free, scamper, six_hats, random_word, reverse")] string technique = "free",
        [Description("Number of ideas to generate")] int count = 10)
    {
        var techDesc = technique.ToLower() switch
        {
            "scamper"     => "using the SCAMPER technique (Substitute, Combine, Adapt, Modify, Put to other uses, Eliminate, Reverse)",
            "six_hats"    => "using Edward de Bono's Six Thinking Hats framework",
            "random_word" => "by combining the topic with unexpected random words or concepts",
            "reverse"     => "by reversing the problem (how could we FAIL at this?), then inverting the answers",
            _             => "freely without constraints"
        };
        return await Prompt($"Generate {count} creative ideas about '{topic}' {techDesc}. Number each idea and add a brief explanation.");
    }

    [KernelFunction, Description("Evaluate the quality of reasoning or arguments in a text (identify logical fallacies, weaknesses)")]
    public async Task<string> EvaluateArgumentAsync([Description("Argument or text to evaluate")] string argument)
    {
        return await Prompt($"""
            Critically evaluate the following argument. Identify:
            1. Main claim and supporting premises
            2. Logical fallacies (if any)
            3. Unsupported assumptions
            4. Missing evidence
            5. Counter-arguments
            6. Overall strength (Weak/Moderate/Strong) and why
            
            Argument: {argument}
            """);
    }

    [KernelFunction, Description("Simulate a debate with arguments for and against a position")]
    public async Task<string> DebateAsync(
        [Description("Topic or position to debate")] string topic,
        [Description("Number of arguments per side")] int argumentsPerSide = 3)
    {
        return await Prompt($"""
            Present a structured debate on: "{topic}"
            
            FOR (supporting arguments):
            {string.Join("\n", Enumerable.Range(1, argumentsPerSide).Select(i => $"{i}."))}
            
            AGAINST (opposing arguments):
            {string.Join("\n", Enumerable.Range(1, argumentsPerSide).Select(i => $"{i}."))}
            
            CONCLUSION: A balanced synthesis of both sides.
            
            Topic: {topic}
            """);
    }

    [KernelFunction, Description("Ask Socratic questions to deepen understanding of a topic")]
    public async Task<string> SocraticQuestionsAsync(
        [Description("Topic, belief, or statement to explore")] string statement,
        [Description("Number of probing questions")] int count = 5)
    {
        return await Prompt($"Generate {count} probing Socratic questions to deepen critical thinking about: '{statement}'. Questions should challenge assumptions, clarify concepts, and explore implications.");
    }

    [KernelFunction, Description("Apply the '5 Whys' root cause analysis technique")]
    public async Task<string> FiveWhysAsync(
        [Description("Problem or issue to investigate")] string problem)
    {
        return await Prompt($"""
            Apply the '5 Whys' root cause analysis to this problem:
            Problem: {problem}
            
            Why 1: (surface cause)
            Why 2: (deeper cause)
            Why 3: ...
            Why 4: ...
            Why 5: (root cause)
            
            Root Cause Summary:
            Recommended Solution:
            """);
    }

    [KernelFunction, Description("Perform a pre-mortem analysis: imagine a project has failed and identify why")]
    public async Task<string> PreMortemAsync(
        [Description("Project, plan, or decision")] string project)
    {
        return await Prompt($"""
            Perform a pre-mortem analysis. Imagine it's 1 year from now and '{project}' has completely failed.
            
            1. What went wrong? List 7-10 specific failure modes.
            2. Which failures were most predictable?
            3. What early warning signs would appear?
            4. What preventive measures should be taken NOW?
            """);
    }

    private async Task<string> Prompt(string text)
    {
        var result = await _kernel.InvokePromptAsync(text);
        return result.GetValue<string>() ?? "";
    }
}

/// <summary>
/// ContentSkill — AI-powered content generation for writing, marketing, and communication.
/// </summary>
public class ContentSkill
{
    private readonly Kernel _kernel;
    public ContentSkill(Kernel kernel) => _kernel = kernel;

    [KernelFunction, Description("Write a blog post or article on a topic")]
    public async Task<string> WriteBlogPostAsync(
        [Description("Topic or title")] string topic,
        [Description("Target audience")] string audience = "general readers",
        [Description("Tone: informative, persuasive, entertaining, technical")] string tone = "informative",
        [Description("Approximate word count")] int wordCount = 500)
    {
        return await Prompt($"Write a {wordCount}-word {tone} blog post about '{topic}' for {audience}. Include an engaging title, introduction, 3-4 sections with subheadings, and a conclusion.");
    }

    [KernelFunction, Description("Write an email for a specific purpose")]
    public async Task<string> WriteEmailAsync(
        [Description("Email purpose, e.g. 'follow up on meeting', 'job application', 'complaint'")] string purpose,
        [Description("Recipient name or title")] string recipient = "",
        [Description("Sender name")] string sender = "",
        [Description("Key points to include")] string keyPoints = "",
        [Description("Tone: professional, friendly, formal, urgent")] string tone = "professional")
    {
        return await Prompt($"""
            Write a {tone} email for the following purpose: {purpose}
            {(string.IsNullOrEmpty(recipient) ? "" : $"To: {recipient}")}
            {(string.IsNullOrEmpty(sender) ? "" : $"From: {sender}")}
            {(string.IsNullOrEmpty(keyPoints) ? "" : $"Key points to include: {keyPoints}")}
            
            Include: Subject line, greeting, body, closing.
            """);
    }

    [KernelFunction, Description("Write social media posts (Twitter/X, LinkedIn, Instagram, etc.)")]
    public async Task<string> WriteSocialPostAsync(
        [Description("Content topic or message")] string topic,
        [Description("Platform: twitter, linkedin, instagram, facebook, tiktok")] string platform = "linkedin",
        [Description("Include hashtags?")] bool includeHashtags = true,
        [Description("Tone: professional, casual, funny, inspirational")] string tone = "professional")
    {
        var limits = platform.ToLower() switch
        {
            "twitter" or "x" => "280 characters",
            "instagram" => "2200 characters, focus on visual storytelling",
            "linkedin" => "3000 characters, professional focus",
            "tiktok" => "150 characters, engaging and trendy",
            _ => "appropriate length"
        };
        return await Prompt($"Write a {tone} {platform} post about '{topic}'. Stay within {limits}. {(includeHashtags ? "Include relevant hashtags." : "No hashtags.")}");
    }

    [KernelFunction, Description("Generate creative story ideas or write a short story")]
    public async Task<string> WriteStoryAsync(
        [Description("Story premise, genre, or theme")] string premise,
        [Description("Genre: mystery, sci-fi, romance, horror, adventure, fantasy, drama")] string genre = "general",
        [Description("Length: micro (100 words), short (500 words), flash (1000 words)")] string length = "short")
    {
        var wordCount = length switch { "micro" => 100, "flash" => 1000, _ => 500 };
        return await Prompt($"Write a {wordCount}-word {genre} story based on this premise: {premise}. Focus on compelling characters, vivid setting, and a satisfying narrative arc.");
    }

    [KernelFunction, Description("Write a product description or marketing copy")]
    public async Task<string> WriteProductDescAsync(
        [Description("Product name and key features")] string product,
        [Description("Target customer")] string targetCustomer = "general consumers",
        [Description("Style: ecommerce, ad copy, technical spec")] string style = "ecommerce")
    {
        return await Prompt($"Write compelling {style} copy for: {product}\nTarget customer: {targetCustomer}\nInclude: headline, key benefits (not just features), call-to-action.");
    }

    [KernelFunction, Description("Create a structured report or document outline")]
    public async Task<string> CreateOutlineAsync(
        [Description("Report topic or document type")] string topic,
        [Description("Document type: report, proposal, thesis, presentation, business_plan")] string docType = "report",
        [Description("Number of main sections")] int sections = 5)
    {
        return await Prompt($"Create a detailed {sections}-section outline for a {docType} on: '{topic}'. Include section titles, subsections, and brief descriptions of content for each part.");
    }

    [KernelFunction, Description("Rewrite content in a different style, tone, or format")]
    public async Task<string> RewriteContentAsync(
        [Description("Original content")] string content,
        [Description("Rewrite as: formal, casual, academic, persuasive, simpler, longer, shorter")] string style)
    {
        return await Prompt($"Rewrite the following content in a {style} style. Preserve the core information but adapt the language, structure, and tone:\n\n{content}");
    }

    private async Task<string> Prompt(string text)
    {
        var result = await _kernel.InvokePromptAsync(text);
        return result.GetValue<string>() ?? "";
    }
}
