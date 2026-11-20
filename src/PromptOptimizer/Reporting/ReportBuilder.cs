using System.Text;
using System.Web;
using PromptOptimizer.Models;

namespace PromptOptimizer.Reporting;

/// <summary>
/// Stage H: Generates the final HTML report with:
/// - Side-by-side prompt comparison with diff highlighting
/// - Score tables (optimization set + held-out, before/after, per criterion)
/// - Per-round history
/// - Failure analysis
/// - Cost and confidence statement
/// </summary>
public sealed class ReportBuilder
{
    /// <summary>
    /// Builds the full HTML report for a completed experiment.
    /// </summary>
    public string Build(Experiment experiment)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine($"<title>Prompt Optimizer Report — {HttpUtility.HtmlEncode(experiment.Id)}</title>");
        sb.AppendLine(GetStyles());
        sb.AppendLine("</head><body>");
        sb.AppendLine($"<h1>🔬 Prompt Optimization Report</h1>");
        sb.AppendLine($"<p class=\"meta\">Experiment: <strong>{HttpUtility.HtmlEncode(experiment.Id)}</strong> | " +
                      $"Created: {experiment.CreatedAt:yyyy-MM-dd HH:mm} | " +
                      $"Total cost: ${experiment.TotalCost:F4}</p>");

        // Suitability
        if (experiment.Suitability is not null)
        {
            sb.AppendLine("<h2>📋 Suitability</h2>");
            sb.AppendLine($"<p><strong>{experiment.Suitability.Level}</strong>: {HttpUtility.HtmlEncode(experiment.Suitability.Reason)}</p>");
        }

        // Side-by-side prompts
        sb.AppendLine(BuildPromptComparison(experiment));

        // Score summary
        sb.AppendLine(BuildScoreSummary(experiment));

        // Round history
        sb.AppendLine(BuildRoundHistory(experiment));

        // Held-out validation
        if (experiment.HeldOutResult is not null)
        {
            sb.AppendLine(BuildHeldOutSection(experiment.HeldOutResult));
        }

        // Confidence statement
        sb.AppendLine(BuildConfidenceStatement(experiment));

        sb.AppendLine(GetScript());
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string BuildPromptComparison(Experiment experiment)
    {
        var original = experiment.Candidates.FirstOrDefault(c => c.Round == 0);
        var best = experiment.Candidates.OrderByDescending(c => c.Round).FirstOrDefault();

        if (original is null || best is null) return "";

        return $"""
            <h2>📝 Prompt Comparison</h2>
            <div class="comparison">
              <div class="prompt-box original">
                <div class="prompt-box-header">
                  <h3>Original (Round 0)</h3>
                  <button type="button" class="copy-btn" onclick="copyPrompt('original-prompt-text', this)" title="Copy original prompt">
                    <span>📋 Copy</span>
                  </button>
                </div>
                <pre id="original-prompt-text">{HttpUtility.HtmlEncode(original.Text)}</pre>
              </div>
              <div class="prompt-box optimized">
                <div class="prompt-box-header">
                  <h3>Optimized (Round {best.Round})</h3>
                  <button type="button" class="copy-btn copy-btn-primary" onclick="copyPrompt('optimized-prompt-text', this)" title="Copy optimized prompt">
                    <span>📋 Copy Prompt</span>
                  </button>
                </div>
                <pre id="optimized-prompt-text">{HttpUtility.HtmlEncode(best.Text)}</pre>
                {(best.ChangeLog is not null ? $"<p class=\"changelog\"><strong>Changes:</strong> {HttpUtility.HtmlEncode(best.ChangeLog)}</p>" : "")}
              </div>
            </div>
            """;
    }

    private static string BuildScoreSummary(Experiment experiment)
    {
        if (experiment.RoundSummaries.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("<h2>📊 Score Summary</h2>");
        sb.AppendLine("<table><thead><tr><th>Round</th><th>Candidate</th><th>Mean Score</th>");

        // Add criterion columns
        var criteria = experiment.RoundSummaries[0].CriterionMeans;
        foreach (var c in criteria)
        {
            sb.AppendLine($"<th>{HttpUtility.HtmlEncode(c.CriterionId)}</th>");
        }
        sb.AppendLine("<th>Cost</th></tr></thead><tbody>");

        foreach (var round in experiment.RoundSummaries)
        {
            sb.Append($"<tr><td>{round.Round}</td><td>{HttpUtility.HtmlEncode(round.CandidateId)}</td>");
            sb.Append($"<td class=\"{ScoreClass(round.MeanScore)}\">{round.MeanScore:F3}</td>");

            foreach (var cm in round.CriterionMeans)
            {
                sb.Append($"<td class=\"{ScoreClass(cm.MeanScore)}\">{cm.MeanScore:F2} ({cm.PassCount}/{cm.TotalCount})</td>");
            }

            sb.AppendLine($"<td>${round.CumulativeCost:F4}</td></tr>");
        }

        sb.AppendLine("</tbody></table>");
        return sb.ToString();
    }

    private static string BuildRoundHistory(Experiment experiment)
    {
        if (experiment.RoundSummaries.Count < 2) return "";

        var sb = new StringBuilder();
        sb.AppendLine("<h2>📈 Optimization History & Progress</h2>");
        sb.AppendLine("<div class=\"history-card\">");
        sb.AppendLine("  <p class=\"history-desc\">");
        sb.AppendLine("    Visualizes score progression across optimization rounds. Each bar represents the prompt candidate's <strong>overall mean score</strong> (scaled 0.000 to 1.000 / 0% to 100%) across all test cases. Higher scores indicate closer alignment with rubric criteria.");
        sb.AppendLine("  </p>");

        var targetScore = Math.Clamp(experiment.TargetScore, 0.0, 1.0);
        var targetPercent = targetScore * 100;
        var hasTarget = targetScore > 0.0;

        // Legend explaining bar meanings and colors
        sb.AppendLine("  <div class=\"history-legend\">");
        sb.AppendLine("    <div class=\"legend-item\"><span class=\"legend-swatch bar-high\"></span> <strong>High (≥ 0.80)</strong> — Target quality</div>");
        sb.AppendLine("    <div class=\"legend-item\"><span class=\"legend-swatch bar-mid\"></span> <strong>Moderate (0.50 – 0.79)</strong> — Needs refinement</div>");
        sb.AppendLine("    <div class=\"legend-item\"><span class=\"legend-swatch bar-low\"></span> <strong>Low (&lt; 0.50)</strong> — High failure rate</div>");
        if (hasTarget)
        {
            sb.AppendLine($"    <div class=\"legend-item\"><span class=\"legend-target-line\"></span> <strong>Target Goal ({targetScore:F2} / {targetPercent:F0}%)</strong></div>");
        }
        sb.AppendLine("  </div>");

        sb.AppendLine("  <div class=\"round-history-list\">");

        var baselineScore = experiment.RoundSummaries[0].MeanScore;

        for (var i = 0; i < experiment.RoundSummaries.Count; i++)
        {
            var round = experiment.RoundSummaries[i];
            var clampedScore = Math.Clamp(round.MeanScore, 0.0, 1.0);
            var barWidth = (int)Math.Round(clampedScore * 100);
            var scorePercent = clampedScore * 100;
            var barClass = HistoryBarClass(round.MeanScore);

            // Delta calculation
            string deltaHtml;
            if (i == 0)
            {
                deltaHtml = "<span class=\"delta-pill delta-neutral\" title=\"Baseline prompt (Round 0)\">Baseline</span>";
            }
            else
            {
                var deltaVsBaseline = round.MeanScore - baselineScore;
                var deltaPercent = deltaVsBaseline * 100;
                if (deltaVsBaseline > 0.0005)
                {
                    deltaHtml = $"<span class=\"delta-pill delta-positive\" title=\"Improvement over baseline\">▲ +{deltaVsBaseline:F3} (+{deltaPercent:F1}%) vs R0</span>";
                }
                else if (deltaVsBaseline < -0.0005)
                {
                    deltaHtml = $"<span class=\"delta-pill delta-negative\" title=\"Regression compared to baseline\">▼ {deltaVsBaseline:F3} ({deltaPercent:F1}%) vs R0</span>";
                }
                else
                {
                    deltaHtml = "<span class=\"delta-pill delta-neutral\" title=\"No change from baseline\">— 0.000 vs R0</span>";
                }

                if (i > 1)
                {
                    var prevRound = experiment.RoundSummaries[i - 1];
                    var deltaVsPrev = round.MeanScore - prevRound.MeanScore;
                    var prevText = deltaVsPrev > 0.0005
                        ? $"▲ +{deltaVsPrev:F3}"
                        : deltaVsPrev < -0.0005
                            ? $"▼ {deltaVsPrev:F3}"
                            : "— 0.000";
                    deltaHtml += $" <span class=\"delta-prev\" title=\"Change vs previous round\">({prevText} vs R{prevRound.Round})</span>";
                }
            }

            // Criteria stats
            var criteriaHtml = "";
            if (round.CriterionMeans.Count > 0)
            {
                var passed = round.CriterionMeans.Count(c => c.MeanScore >= 0.999 || (c.TotalCount > 0 && c.PassCount == c.TotalCount));
                var total = round.CriterionMeans.Count;
                criteriaHtml = $"<span class=\"metric-criteria\" title=\"Criteria completely passed\">Criteria: <strong>{passed}/{total}</strong> passed</span>";
            }

            // Candidate changelog
            var candidate = experiment.Candidates.FirstOrDefault(c => c.Round == round.Round);
            var changelogHtml = "";
            if (!string.IsNullOrWhiteSpace(candidate?.ChangeLog))
            {
                changelogHtml = $"""
                    <div class="round-changelog">
                      <strong>Mutation:</strong> {HttpUtility.HtmlEncode(candidate.ChangeLog)}
                    </div>
                    """;
            }

            var isWide = barWidth >= 22;
            var scoreText = $"{round.MeanScore:F3} ({scorePercent:F1}%)";
            var targetMarkerHtml = hasTarget
                ? $"<div class=\"target-line\" style=\"left: {targetPercent:F1}%;\" title=\"Target Goal: {targetScore:F2} ({targetPercent:F0}%)\"></div>"
                : "";

            sb.AppendLine($"""
                <div class="round-card">
                  <div class="round-card-header">
                    <div class="round-title">
                      <span class="round-badge">Round {round.Round}</span>
                      <span class="candidate-pill">{HttpUtility.HtmlEncode(round.CandidateId)}</span>
                      {(i == 0 ? "<span class=\"baseline-pill\">Baseline</span>" : "")}
                    </div>
                    <div class="round-metrics">
                      <span class="metric-score">Score: <strong>{round.MeanScore:F3}</strong> <span class="metric-percent">({scorePercent:F1}%)</span></span>
                      {deltaHtml}
                      {criteriaHtml}
                      <span class="metric-cost">Cost: ${round.CumulativeCost:F4}</span>
                    </div>
                  </div>

                  <div class="bar-track-wrapper">
                    <div class="bar-track">
                      <div class="bar-fill {barClass}" style="width: {barWidth}%;" title="Round {round.Round} ({HttpUtility.HtmlEncode(round.CandidateId)}): Mean score {scoreText}">
                        {(isWide ? $"<span class=\"bar-label inside\">{scoreText}</span>" : "")}
                      </div>
                      {(!isWide ? $"<span class=\"bar-label outside\" style=\"left: calc({barWidth}% + 8px);\">{scoreText}</span>" : "")}
                      {targetMarkerHtml}
                    </div>
                  </div>

                  {changelogHtml}
                </div>
                """);
        }

        // Axis ruler
        sb.AppendLine("    <div class=\"history-axis\">");
        sb.AppendLine("      <div class=\"axis-scale\">");
        sb.AppendLine("        <div class=\"axis-mark mark-start\"><span>0.0 (0%)</span></div>");
        sb.AppendLine("        <div class=\"axis-mark\" style=\"left: 25%;\"><span>0.25 (25%)</span></div>");
        sb.AppendLine("        <div class=\"axis-mark\" style=\"left: 50%;\"><span>0.50 (50%)</span></div>");
        sb.AppendLine("        <div class=\"axis-mark\" style=\"left: 75%;\"><span>0.75 (75%)</span></div>");
        sb.AppendLine("        <div class=\"axis-mark mark-end\"><span>1.0 (100%)</span></div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");

        sb.AppendLine("  </div>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string BuildHeldOutSection(HeldOutResult heldOut)
    {
        var gapClass = Math.Abs(heldOut.OverfitGap) > 0.1 ? "warning" : "good";

        return $"""
            <h2>🔒 Held-Out Validation</h2>
            <table>
              <tr><th></th><th>Original</th><th>Optimized</th><th>Δ</th></tr>
              <tr>
                <td>Held-out score</td>
                <td>{heldOut.OriginalScore:F3}</td>
                <td class="{ScoreClass(heldOut.OptimizedScore)}">{heldOut.OptimizedScore:F3}</td>
                <td>{heldOut.OptimizedScore - heldOut.OriginalScore:+0.000;-0.000}</td>
              </tr>
            </table>
            <p class="{gapClass}">Overfit gap: {heldOut.OverfitGap:+0.000;-0.000}
            {(Math.Abs(heldOut.OverfitGap) > 0.1 ? " ⚠️ Large gap suggests overfitting to the optimization set." : " ✅ Gap is within acceptable range.")}</p>
            """;
    }

    private static string BuildConfidenceStatement(Experiment experiment)
    {
        var statements = new List<string>
        {
            "Scores are based on a small test set and should be interpreted as directional, not exact.",
            "The LLM judge may have biases (e.g. favoring longer/more polished outputs). Deterministic checks mitigate this where possible."
        };

        if (experiment.HeldOutResult is { OverfitGap: > 0.1 })
        {
            statements.Add("⚠️ The overfit gap is significant. The optimization-set improvement may not fully generalize.");
        }

        return $"""
            <h2>⚖️ Confidence Statement</h2>
            <ul>
            {string.Join("\n", statements.Select(s => $"  <li>{HttpUtility.HtmlEncode(s)}</li>"))}
            </ul>
            """;
    }

    private static string ScoreClass(double score) => score switch
    {
        >= 0.8 => "score-high",
        >= 0.5 => "score-mid",
        _ => "score-low"
    };

    private static string HistoryBarClass(double score) => score switch
    {
        >= 0.8 => "bar-high",
        >= 0.5 => "bar-mid",
        _ => "bar-low"
    };

    private static string GetStyles() =>
        """
        <style>
          body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif; max-width: 1200px; margin: 0 auto; padding: 2rem; color: #1a1a2e; background: #f5f5f7; }
          h1 { color: #16213e; border-bottom: 3px solid #0f3460; padding-bottom: 0.5rem; }
          h2 { color: #16213e; margin-top: 2rem; }
          .meta { color: #666; font-size: 0.9rem; }
          table { border-collapse: collapse; width: 100%; margin: 1rem 0; }
          th, td { border: 1px solid #ddd; padding: 8px 12px; text-align: left; }
          th { background: #16213e; color: white; }
          tr:nth-child(even) { background: #f9f9f9; }
          .score-high { background: #d4edda; color: #155724; font-weight: bold; }
          .score-mid { background: #fff3cd; color: #856404; }
          .score-low { background: #f8d7da; color: #721c24; }
          .comparison { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; }
          .prompt-box { background: white; border-radius: 8px; padding: 1rem; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
          .prompt-box-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem; }
          .prompt-box-header h3 { margin: 0; }
          .copy-btn {
            display: inline-flex;
            align-items: center;
            gap: 0.35rem;
            padding: 5px 12px;
            font-size: 0.8rem;
            font-weight: 600;
            color: #0f3460;
            background: #eef2f6;
            border: 1px solid #cbd5e1;
            border-radius: 6px;
            cursor: pointer;
            transition: all 0.2s ease;
          }
          .copy-btn:hover { background: #e2e8f0; border-color: #94a3b8; }
          .copy-btn-primary { background: #28a745; color: #ffffff; border-color: #218838; }
          .copy-btn-primary:hover { background: #218838; border-color: #1e7e34; }
          .copy-btn.copied, .copy-btn-primary.copied { background: #155724 !important; color: #ffffff !important; border-color: #0e3e18 !important; }
          .prompt-box pre { white-space: pre-wrap; word-wrap: break-word; background: #f8f9fa; padding: 1rem; border-radius: 4px; font-size: 0.85rem; }
          .original h3 { color: #6c757d; }
          .optimized h3 { color: #28a745; }
          .changelog { font-size: 0.85rem; color: #495057; background: #e9ecef; padding: 0.5rem; border-radius: 4px; }
          .warning { color: #856404; background: #fff3cd; padding: 0.5rem; border-radius: 4px; }
          .good { color: #155724; background: #d4edda; padding: 0.5rem; border-radius: 4px; }

          /* Optimization History styles */
          .history-card { background: white; border-radius: 8px; padding: 1.25rem; box-shadow: 0 2px 4px rgba(0,0,0,0.08); margin: 1rem 0 2rem 0; }
          .history-desc { color: #4b5563; font-size: 0.92rem; margin: 0 0 0.75rem 0; line-height: 1.5; }
          .history-legend { display: flex; flex-wrap: wrap; gap: 1rem; align-items: center; margin: 0.75rem 0 1.25rem 0; padding: 0.65rem 1rem; background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; font-size: 0.85rem; }
          .legend-item { display: inline-flex; align-items: center; gap: 0.45rem; color: #334155; }
          .legend-swatch { width: 14px; height: 14px; border-radius: 3px; display: inline-block; }
          .legend-target-line { width: 16px; height: 0; border-top: 2px dashed #0f3460; display: inline-block; }
          .round-history-list { display: flex; flex-direction: column; gap: 0.75rem; }
          .round-card { background: #fafafa; border: 1px solid #e2e8f0; border-radius: 8px; padding: 0.85rem 1rem; }
          .round-card-header { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 0.5rem; margin-bottom: 0.4rem; }
          .round-title { display: flex; align-items: center; gap: 0.5rem; }
          .round-badge { font-weight: 700; font-size: 0.9rem; color: #0f3460; }
          .candidate-pill { background: #e2e8f0; color: #475569; font-size: 0.78rem; padding: 2px 6px; border-radius: 4px; font-family: monospace; }
          .baseline-pill { background: #0f3460; color: white; font-size: 0.72rem; padding: 2px 6px; border-radius: 4px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; }
          .round-metrics { display: flex; align-items: center; flex-wrap: wrap; gap: 0.65rem; font-size: 0.82rem; }
          .metric-score strong { color: #0f3460; font-size: 0.95rem; }
          .metric-percent { color: #64748b; font-size: 0.8rem; }
          .delta-pill { padding: 2px 6px; border-radius: 4px; font-size: 0.75rem; font-weight: 600; }
          .delta-positive { background: #d4edda; color: #155724; }
          .delta-negative { background: #f8d7da; color: #721c24; }
          .delta-neutral { background: #e2e8f0; color: #475569; }
          .delta-prev { font-size: 0.75rem; color: #64748b; }
          .metric-criteria { color: #475569; }
          .metric-cost { color: #64748b; }
          .bar-track-wrapper { position: relative; margin: 6px 0; }
          .bar-track { position: relative; background: #e2e8f0; border-radius: 6px; height: 26px; display: flex; align-items: center; overflow: visible; }
          .bar-fill { height: 100%; border-radius: 6px; display: flex; align-items: center; justify-content: flex-end; padding: 0 8px; box-sizing: border-box; font-weight: 600; font-size: 0.8rem; min-width: 3px; }
          .bar-high { background: linear-gradient(90deg, #28a745, #34ce57); color: #ffffff; text-shadow: 0 1px 2px rgba(0,0,0,0.3); }
          .bar-mid { background: linear-gradient(90deg, #e0a800, #ffc107); color: #1a1a2e; }
          .bar-low { background: linear-gradient(90deg, #dc3545, #e4606d); color: #ffffff; text-shadow: 0 1px 2px rgba(0,0,0,0.3); }
          .bar-label.inside { white-space: nowrap; }
          .bar-label.outside { position: absolute; font-size: 0.8rem; font-weight: 600; color: #1e293b; white-space: nowrap; }
          .target-line { position: absolute; top: -3px; bottom: -3px; width: 0; border-left: 2px dashed #0f3460; z-index: 2; pointer-events: none; }
          .round-changelog { margin-top: 0.5rem; padding: 0.5rem 0.75rem; background: #f1f5f9; border-left: 3px solid #3b82f6; border-radius: 0 4px 4px 0; font-size: 0.82rem; color: #334155; line-height: 1.4; }
          .history-axis { position: relative; margin-top: 6px; padding-top: 4px; }
          .axis-scale { position: relative; height: 22px; border-top: 1px solid #cbd5e1; }
          .axis-mark { position: absolute; top: 0; transform: translateX(-50%); font-size: 0.75rem; color: #64748b; padding-top: 3px; }
          .axis-mark::before { content: ""; position: absolute; top: 0; left: 50%; width: 1px; height: 4px; background: #cbd5e1; }
          .axis-mark.mark-start { transform: translateX(0); left: 0; }
          .axis-mark.mark-start::before { left: 0; }
          .axis-mark.mark-end { transform: translateX(-100%); left: 100%; }
          .axis-mark.mark-end::before { left: 100%; }

          /* Backward compatibility */
          .round-history { margin: 1rem 0; }
          .round-bar { display: flex; align-items: center; margin: 4px 0; }
          .round-label { width: 3rem; font-weight: bold; }
          .bar-container { flex: 1; background: #e9ecef; border-radius: 4px; height: 24px; }
          .bar { height: 100%; border-radius: 4px; display: flex; align-items: center; padding: 0 8px; font-size: 0.8rem; color: white; min-width: 60px; }
        </style>
        """;

    private static string GetScript() =>
        """
        <script>
          function copyPrompt(elementId, btn) {
            const el = document.getElementById(elementId);
            if (!el) return;
            const text = el.innerText;
            const fallback = () => {
              const textarea = document.createElement('textarea');
              textarea.value = text;
              textarea.style.position = 'fixed';
              textarea.style.opacity = '0';
              document.body.appendChild(textarea);
              textarea.select();
              try {
                document.execCommand('copy');
                showCopied(btn);
              } catch (e) {
                console.error('Copy fallback failed', e);
              }
              document.body.removeChild(textarea);
            };

            if (navigator.clipboard && navigator.clipboard.writeText) {
              navigator.clipboard.writeText(text).then(() => showCopied(btn)).catch(fallback);
            } else {
              fallback();
            }
          }

          function showCopied(btn) {
            const orig = btn.innerHTML;
            btn.innerHTML = '<span>✅ Copied!</span>';
            btn.classList.add('copied');
            setTimeout(() => {
              btn.innerHTML = orig;
              btn.classList.remove('copied');
            }, 2000);
          }
        </script>
        """;
}
