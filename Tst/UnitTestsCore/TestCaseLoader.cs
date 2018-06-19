using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace UnitTestsCore
{
    public class TestCaseLoader
    {
        //private static readonly List<string> TestDirs = new List<string> { "RegressionTests" };
        private static readonly List<string> TestDirs = new List<string> { "RegressionTests\\Combined",
           "RegressionTests\\Feature1SMLevelDecls",  "RegressionTests\\Feature2Stmts", "RegressionTests\\Feature3Exprs",
           "RegressionTests\\Feature4DataTypes", "RegressionTests\\Integration"
            };
        //To run Liveness tests, set Settings.PtWithPSharp to true:
        //private static readonly List<string> TestDirs = new List<string> { "Liveness" };
        public static IEnumerable<TestCaseData> FindTestCasesInDirectory(string directoryName)
        {
            //Remove previous TestResultsDirectory:
            try
            {
                if (Directory.Exists(Constants.TestResultsDirectory))
                {
                    Directory.Delete(Constants.TestResultsDirectory, true);
                }
            }
            catch (Exception e)
            {
                ConsoleHelper.WriteError("ERROR: Could not delete old test directory: {0}", e.Message);
            }

            //Remove old file with diffs:
            File.Delete(Path.Combine(Constants.TestDirectory, Constants.DisplayDiffsFile));

            return from testDir in TestDirs
                   let baseDirectory = new DirectoryInfo(Path.Combine(directoryName, testDir))
                   from testCaseDir in baseDirectory.EnumerateDirectories("*.*", SearchOption.AllDirectories)
                   where testCaseDir.GetDirectories().Any(info => Enum.GetNames(typeof(TestType)).Contains(info.Name))
                   select DirectoryToTestCase(testCaseDir, baseDirectory);
        }

        private static TestCaseData DirectoryToTestCase(DirectoryInfo dir, DirectoryInfo testRoot)
        {
            var variables = GetVariables(testRoot);
            var testConfigs =
                (from type in Enum.GetValues(typeof(TestType)).Cast<TestType>()
                 let configPath = Path.Combine(dir.FullName, type.ToString(), Constants.TestConfigFileName)
                 where File.Exists(configPath)
                 select new { type, config = ParseTestConfig(configPath, variables) })
                .ToDictionary(kv => kv.type, kv => kv.config);

            var category = testRoot.Name + Constants.CategorySeparator + GetCategory(dir, testRoot);
            return new TestCaseData(dir, testConfigs)
                .SetName(category + Constants.CategorySeparator + dir.Name)
                .SetCategory(category);
        }

        private static Dictionary<string, string> GetVariables(DirectoryInfo testRoot)
        {
            var binDir = Path.Combine(Constants.SolutionDirectory, "bld", "drops", Constants.BuildConfiguration, Constants.Platform, "binaries");
            var variables = new Dictionary<string, string>
            {
                {"platform", Constants.Platform},
                {"testroot", testRoot.FullName},
                {"configuration", Constants.BuildConfiguration},
                {"testbinaries", binDir}
            };
            return variables;
        }

        private static string GetCategory(DirectoryInfo dir, DirectoryInfo baseDirectory)
        {
            var category = "";
            var sep = "";
            dir = dir.Parent;
            while (dir != null && dir.FullName != baseDirectory.FullName)
            {
                //category = $"{category}{sep}{dir.Name}";
                category = $"{dir.Name}{sep}{category}";
                dir = dir.Parent;
                sep = Constants.CategorySeparator;
            }
            return category;
        }

        private static TestConfig ParseTestConfig(string testConfigPath, IDictionary<string, string> variables)
        {
            var testConfig = new TestConfig();

            foreach (var assignment in File.ReadLines(testConfigPath))
            {
                if (string.IsNullOrWhiteSpace(assignment))
                {
                    continue;
                }

                var parts = assignment.Split(new[] { ':' }, 2).Select(x => x.Trim()).ToArray();
                var key = parts[0];
                var value = SubstituteVariables(parts[1], variables);
                switch (key)
                {
                    case "inc":
                        testConfig.Includes.Add(value);
                        break;
                    case "del":
                        testConfig.Deletes.Add(value);
                        break;
                    case "arg":
                        testConfig.Arguments.Add(value);
                        break;
                    case "dsc":
                        testConfig.Description = value;
                        break;
                    case "link":
                        testConfig.Link = value;
                        break;
                    default:
                        Debug.WriteLine($"Unrecognized option '{key}' in file ${testConfigPath}");
                        break;
                }
            }

            return testConfig;
        }

        private static string SubstituteVariables(string value, IDictionary<string, string> variables)
        {
            // Replaces variables that use a syntax like $(VarName). Inner capture group gets the name.
            return Regex.Replace(value, @"\$\(([^)]+)\)", match =>
            {
                var variableName = match.Groups[1].Value.ToLowerInvariant();
                return variables.TryGetValue(variableName, out var variableValue) ? variableValue : match.Value;
            });
        }
    }
}