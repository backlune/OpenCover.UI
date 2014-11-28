using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using Microsoft.Win32;
using OpenCover.UI.Helpers;
using OpenCover.UI.Model;
using OpenCover.UI.Model.Test;

namespace OpenCover.UI.Processors
{
    internal class XUnitTestExecutor : TestExecutor
    {

        private string _runListFile;
		private string _xUnitPath;

		/// <summary>
		/// Initializes a new instance of the <see cref="XUnitTestExecutor"/> class.
		/// </summary>
		/// <param name="package">The package.</param>
		/// <param name="selectedTests">The selected tests.</param>
		internal XUnitTestExecutor(OpenCoverUIPackage package, Tuple<IEnumerable<String>, IEnumerable<String>, IEnumerable<String>> selectedTests)
			: base(package, selectedTests)
		{
			SetXUnitPath();

			_executionStatus = new Dictionary<string, IEnumerable<TestResult>>();
		}

		/// <summary>
		/// Sets the OpenCover commandline arguments.
		/// </summary>
		protected override void SetOpenCoverCommandlineArguments()
		{
			var fileFormat = DateTime.Now.ToString("yyyy_MM_dd_hh_mm_ss_ms");
			var dllPaths = BuildDLLPath();

			SetOpenCoverResultsFilePath();

			_testResultsFile = Path.Combine(_currentWorkingDirectory.FullName, String.Format("{0}.xml", fileFormat));
			_runListFile = Path.Combine(_currentWorkingDirectory.FullName, String.Format("{0}.txt", fileFormat));

			_commandLineArguments = String.Format(CommandlineStringFormat,
													_xUnitPath,
													String.Format("{0} /runlist=\\\"{1}\\\" /nologo /noshadow /result=\\\"{2}\\\"", dllPaths, _runListFile, _testResultsFile),
													_openCoverResultsFile);

			CreateRunListFile();
		}

		/// <summary>
		/// Reads the test results file.
		/// </summary>
		protected override void ReadTestResults()
		{
			try
			{
				if (File.Exists(_testResultsFile))
				{
					var testResultsFile = XDocument.Load(_testResultsFile);

					_executionStatus.Clear();

				    var assemblies = testResultsFile.Descendants("Module");

					foreach (var assembly in assemblies)
					{
						var testClasses = assembly.Descendants("Class");
						var testMethods = testClasses.Elements("results").Elements("test-case").Select(tc => GetTestResult(tc, null));
						testMethods = testMethods.Union(testClasses.Elements("results").Elements("test-suite").Select(ts => ReadTestCase(ts)));

						_executionStatus.Add(assembly.Attribute("name").Value, testMethods);
					}
				}
				else
				{
					IDEHelper.WriteToOutputWindow("Test Results File does not exist: {0}", _testResultsFile);
				}
			}
			catch (Exception ex)
			{
				IDEHelper.WriteToOutputWindow(ex.Message);
				IDEHelper.WriteToOutputWindow(ex.StackTrace);
			}
		}

		/// <summary>
		/// Updates the test methods execution.
		/// </summary>
		internal override void UpdateTestMethodsExecution(IEnumerable<TestClass> tests)
		{
			var execution = _executionStatus.SelectMany(t => t.Value.Select(tm => new { dll = t.Key, result = tm }));

			var executedTests = tests.SelectMany(t => t.TestMethods)
										.Join(execution,
												t => new { d = t.Class.DLLPath, n = t.FullyQualifiedName },
												t => new { d = t.dll, n = t.result.MethodName },
												(testMethod, result) => new { TestMethod = testMethod, Result = result });

			foreach (var test in executedTests)
			{
				test.TestMethod.ExecutionResult = test.Result.result;
			}
		}


		/// <summary>
		/// Delete temporary files created.
		/// </summary>
		internal override void Cleanup()
		{
			base.Cleanup();

			if (File.Exists(_runListFile))
			{
				File.Delete(_runListFile);
			}
		}

	   private IEnumerable<DirectoryInfo> ProgramFilesFolders()
	   {
	      var path = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
	      if (Environment.Is64BitOperatingSystem)
	      {
	         if (path.EndsWith(" (x86)"))
	         {
	            yield return new DirectoryInfo(path.Replace(" (x86)", ""));
	         }
	         else
	         {
	            yield return new DirectoryInfo(path + " (x86)");
	         }
	      }
         yield return new DirectoryInfo(path);
	   }

		/// <summary>
		/// Sets the NUnit path.
		/// </summary>
		private void SetXUnitPath()
		{
		   _xUnitPath = OpenCoverUISettings.Default.XUnitPath;
		   if (!File.Exists(_xUnitPath))
		   {
		      var nunits =
		         from programDir in ProgramFilesFolders()
		         from xunitDir in programDir.GetDirectories("XUnit*")
		         orderby xunitDir.LastWriteTime descending
		         let nunitPath = Path.Combine(xunitDir.FullName, "xunit-console.exe")
		         where File.Exists(nunitPath)
		         select nunitPath;

		      _xUnitPath = nunits.FirstOrDefault();               
		         
		      if (_xUnitPath == null)
		      {
		         MessageBox.Show("XUnit not found at its default path. Please select the Xunit executable",
		            Resources.MessageBoxTitle, MessageBoxButton.OK);
		         var dialog = new OpenFileDialog {Filter = "Executables (*.exe)|*.exe"};
		         if (dialog.ShowDialog() == true)
		         {
		            _xUnitPath = dialog.FileName;
		            OpenCoverUISettings.Default.XUnitPath = _xUnitPath;
		            OpenCoverUISettings.Default.Save();
		         }
		      }
		   }
		}

		/// <summary>
		/// Creates the run list file.
		/// </summary>
		private void CreateRunListFile()
		{
			using (var file = File.OpenWrite(_runListFile))
			{
				using (var writer = new StreamWriter(file))
				{
					foreach (var test in _selectedTests.Item2)
					{
						writer.WriteLine(test);
					}
				}
			}
		}

		/// <summary>
		/// Reads the test case.
		/// </summary>
		/// <param name="ts">The test-suite element.</param>
		private TestResult ReadTestCase(XElement ts)
		{
			var testCasesInTestSuite = ts.Element("results").Elements("test-case");
			var testResults = new List<TestResult>();

			foreach (var testCase in testCasesInTestSuite)
			{
				testResults.Add(GetTestResult(testCase, null));
			}

			var testResult = GetTestResult(ts, testResults);
			if (testResults.Any())
			{
				var testCaseName = testResults.First();
				testResult.MethodName = testCaseName.MethodName.Substring(0, testCaseName.MethodName.IndexOf("("));
			}

			return testResult;
		}

		/// <summary>
		/// Gets the test result.
		/// </summary>
		/// <param name="element">The element.</param>
		/// <param name="testCases">The test cases.</param>
		private TestResult GetTestResult(XElement element, List<TestResult> testCases)
		{
			var failure = element.Element("failure");
			decimal tempTime = -1;

			return new TestResult(GetAttributeValue(element, "name"),
								  GetTestExecutionStatus(GetAttributeValue(element, "result")),
								  Decimal.TryParse(GetAttributeValue(element, "time"), out tempTime) ? tempTime : 0,
								  GetElementValue(failure, "message", XNamespace.None),
								  GetElementValue(failure, "stack-trace", XNamespace.None),
								  testCases);
		}

		/// <summary>
		/// Gets the elements by attribute.
		/// </summary>
		/// <typeparam name="T">XContainer derivative</typeparam>
		/// <param name="parent">The parent.</param>
		/// <param name="elementName">Name of the element.</param>
		/// <param name="attributeName">Name of the attribute.</param>
		/// <param name="attributeValue">The attribute value.</param>
		/// <returns></returns>
		private IEnumerable<XElement> GetElementsByAttribute<T>(T parent, string elementName, string attributeName, string attributeValue)
			where T : XContainer
		{
			return parent.Descendants(elementName).Where(ts => ts.Attribute(attributeName) != null && ts.Attribute(attributeName).Value == attributeValue);
		}
    }
}
