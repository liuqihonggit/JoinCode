from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "migrate_mcp_tools_to_cli_nodes",
    REPO / "scripts" / "migrate_mcp_tools_to_cli_nodes.py",
)
assert SPEC is not None and SPEC.loader is not None
MIGRATE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MIGRATE)
POLICY_SPEC = importlib.util.spec_from_file_location(
    "generate_cli_tool_policies",
    REPO / "non_deliverables_tools" / "scripts" / "Generate-CliToolPolicies.py",
)
assert POLICY_SPEC is not None and POLICY_SPEC.loader is not None
POLICY = importlib.util.module_from_spec(POLICY_SPEC)
POLICY_SPEC.loader.exec_module(POLICY)


class WellKnownParamScanTests(unittest.TestCase):
    def parse_source(self, source: str) -> list[dict]:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "Tools.cs"
            path.write_text(source, encoding="utf-8")
            return MIGRATE.parse_file(path, {}, "system")

    def test_underscore_test_directory_is_not_scanned_as_production(self) -> None:
        self.assertFalse(MIGRATE.is_source_file(Path("modules/generators/aot_safety_tests/Tool.cs")))
        self.assertTrue(MIGRATE.is_source_file(Path("modules/tools/gh/handlers/Tool.cs")))

    def test_well_known_parameters_use_enum_metadata(self) -> None:
        tools = self.parse_source("""
[McpTool("gh_pr_checks", "checks", "github", ConcurrencySafe = true)]
public Task GhPrChecksAsync(
    [McpToolParameter(WellKnownParam.PrNumber)] string pr_number,
    [McpToolParameter(WellKnownParam.Limit)] int? limit = null)
""")

        parameters = tools[0]["parameters"]
        self.assertEqual(["pr_number", "limit"], [item["name"] for item in parameters])
        self.assertTrue(parameters[0]["required"])
        self.assertEqual("PR 编号或 URL", parameters[0]["description"])
        self.assertFalse(parameters[1]["required"])
        self.assertEqual("integer", parameters[1]["type"])
        self.assertEqual("30", parameters[1]["default"])
        self.assertEqual("数量限制", parameters[1]["description"])

    def test_explicit_default_overrides_well_known_default(self) -> None:
        tools = self.parse_source("""
[McpTool("gh_run_list", "runs", "github")]
public Task RunsAsync(
    [McpToolParameter(WellKnownParam.Limit, DefaultValue = "20")] int? limit = null)
""")

        parameter = tools[0]["parameters"][0]
        self.assertEqual("20", parameter["default"])
        self.assertEqual("数量限制", parameter["description"])

    def test_explicit_parameter_wins_over_duplicate_common_option(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Options.cs").write_text("""
public sealed record CommonOptions {
    [McpToolParameter(WellKnownParam.Repo)] public string? Repo { get; }
}
""", encoding="utf-8")
            tool_path = root / "Tools.cs"
            tool_path.write_text("""
[McpTool("gh_repo_clone", "clone", "github")]
public Task CloneAsync(
    [McpToolParameter("仓库名", Required = true)] string repo,
    [McpToolOptions] CommonOptions? common = null)
""", encoding="utf-8")

            options = MIGRATE.collect_options_records(root)
            tools = MIGRATE.parse_file(tool_path, {}, "system", options)

        self.assertEqual(1, len(tools[0]["parameters"]))
        parameter = tools[0]["parameters"][0]
        self.assertEqual("repo", parameter["name"])
        self.assertTrue(parameter["required"])
        self.assertEqual("仓库名", parameter["description"])

    def test_well_known_options_record_property_uses_enum_metadata(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Options.cs").write_text("""
public sealed record SampleOptions {
    [McpToolParameter(WellKnownParam.Limit)] public int? Limit { get; }
}
""", encoding="utf-8")
            tool_path = root / "Tools.cs"
            tool_path.write_text("""
[McpTool("sample", "sample", "system")]
public Task SampleAsync([McpToolOptions] SampleOptions options)
""", encoding="utf-8")

            options = MIGRATE.collect_options_records(root)
            tools = MIGRATE.parse_file(tool_path, {}, "system", options)

        parameter = tools[0]["parameters"][0]
        self.assertEqual("limit", parameter["name"])
        self.assertEqual("30", parameter["default"])
        self.assertFalse(parameter["required"])

    def test_github_help_and_policy_include_required_and_common_parameters(self) -> None:
        help_text = (REPO / "modules/tools/cli_nodes/groups/github/help.txt").read_text(encoding="utf-8")
        _, parameters = POLICY.parse_help(help_text)
        self.assertIn("pr_number", parameters["gh_pr_checks"])
        self.assertIn("repo", parameters["gh_pr_checks"])
        self.assertIn("working_dir", parameters["gh_pr_checks"])

        policy = (REPO / "resources/tools/policies/github.yaml").read_text(encoding="utf-8")
        section = policy.split("  gh_pr_checks:\n", 1)[1].split("\n  gh_", 1)[0]
        self.assertIn("      pr_number:\n        effect: allow", section)
        self.assertIn("      repo:\n        effect: allow", section)

    def test_literal_parameter_description_still_parses(self) -> None:
        tools = self.parse_source("""
[McpTool("sample", "sample", "system")]
public Task SampleAsync(
    [McpToolParameter("自定义描述", Required = false)] string? custom = null)
""")

        parameter = tools[0]["parameters"][0]
        self.assertEqual("custom", parameter["name"])
        self.assertEqual("自定义描述", parameter["description"])
        self.assertFalse(parameter["required"])


if __name__ == "__main__":
    unittest.main()
