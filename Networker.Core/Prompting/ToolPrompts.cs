namespace Networker.Core.Prompting
{
    /// <summary>
    /// System prompts for the AI-assisted ToolsPage features. Each tool sends its
    /// deterministic inputs (config, diff, logs, findings, topology, scenario) to
    /// the model with a purpose-built system prompt. The global system prompt and
    /// custom instructions are appended by <c>ChatService</c> when the request is
    /// built, via <see cref="PromptBuilder.JoinNonEmpty"/>.
    /// </summary>
    public static class ToolPrompts
    {
        /// <summary>
        /// Config Audit: prioritizes the deterministic findings and catches what
        /// the rule-based audit cannot.
        /// </summary>
        public static string ConfigAudit { get; } = """
            You are a senior network engineer reviewing device configurations for security and
            operational soundness. Below is a device configuration followed by the findings of a
            deterministic rule-based audit. Write a prioritized plain-English assessment for the
            engineer: which findings matter most and why, what to fix first, and any significant
            issues the rule-based audit missed. Keep it under 250 words.
            """;

        /// <summary>
        /// Config Diff: explains the semantic impact of a configuration change.
        /// </summary>
        public static string ConfigDiff { get; } = """
            You are a senior network engineer reviewing configuration changes. Below are a baseline
            configuration, a candidate configuration, and a unified diff between them. Explain what
            changed and why it matters: functional impact, risk, and anything to double-check before
            applying. Keep it under 250 words.
            """;

        /// <summary>
        /// Log Analyzer: interprets raw logs plus the deterministic findings into
        /// a root-cause narrative.
        /// </summary>
        public static string LogAnalysis { get; } = """
            You are a senior network engineer analyzing device logs. Below are raw log lines followed
            by the findings of a deterministic log analyzer. Interpret the situation: what is
            happening, the likely root cause, severity, and recommended next steps. Keep it under
            250 words.
            """;

        /// <summary>
        /// Playbooks: asks the model to produce a playbook in the same structured
        /// plain-text format that <c>PlaybookGenerator.RenderPlain</c> emits, so
        /// AI- and rule-generated playbooks render identically.
        /// </summary>
        public static string Playbook { get; } = """
            You are a network engineer writing an actionable troubleshooting or deployment playbook
            for the given scenario. Output a numbered step-by-step playbook in plain text using
            exactly this format for each step:

            Step N: <title>
            <one or more concrete Cisco IOS-style commands>
            Expected: <what a healthy device shows>
            Why: <one-line rationale>

            No markdown, no code fences, no preamble.
            """;

        /// <summary>
        /// Topology: narrates the inferred network from the device configs and the
        /// Mermaid rendering.
        /// </summary>
        public static string Topology { get; } = """
            You are a senior network engineer reading an inferred network topology. Below are the
            device configurations followed by the Mermaid representation of the inferred topology.
            Describe the network: devices and their roles, links and segments, any noteworthy or
            suspicious patterns, and where to look first if there are problems. Keep it under
            250 words.
            """;

        /// <summary>
        /// Translator: faithful multi-vendor translation preserving semantics.
        /// </summary>
        public static string Translation { get; } = """
            You are an expert in multi-vendor network operating systems. Translate the following
            configuration faithfully to the target vendor, preserving semantics: interfaces, VLANs,
            routing protocols, ACLs, and NAT. Where a feature has no direct equivalent, note it
            explicitly. Output the translated configuration first, then brief notes.
            """;
    }
}
