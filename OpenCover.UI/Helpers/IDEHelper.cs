﻿//
// This source code is released under the GPL License; Please read license.md file for more details.
//
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Platform.WindowManagement;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OpenCover.UI.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VSLangProj;
using VSLangProj80;

namespace OpenCover.UI.Helpers
{
	internal static class IDEHelper
	{
		private const string BASE_IMAGE_PREFIX = "/OpenCover.UI;component/";

		private static IVsOutputWindow _outputWindow;
		private static IVsOutputWindowPane _pane;
		private static EnvDTE.DTE DTE;

		/// <summary>
		/// Initializes the <see cref="IDEHelper"/> class.
		/// </summary>
		static IDEHelper()
		{
			_outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;

			Guid guidGeneral = VSConstants.OutputWindowPaneGuid.GeneralPane_guid;
			int hr = _outputWindow.CreatePane(guidGeneral, "OpenCover", 1, 1);
			hr = _outputWindow.GetPane(guidGeneral, out _pane);

			DTE = (Package.GetGlobalService(typeof(EnvDTE.DTE))) as EnvDTE.DTE;
		}

		/// <summary>
		/// Opens the file in Visual Studio.
		/// </summary>
		/// <param name="file">The file path.</param>
		internal static void OpenFile(EnvDTE.DTE DTE, string file)
		{
			try
			{
				if (System.IO.File.Exists(file))
				{
					DTE.ItemOperations.OpenFile(file);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.Message);
			}
		}

		/// <summary>
		/// Closes the file.
		/// </summary>
		/// <param name="DTE">The DTE.</param>
		/// <param name="fileName">Name of the file.</param>
		internal static void CloseFile(EnvDTE.DTE DTE, string fileName)
		{
			foreach (EnvDTE.Document document in DTE.Documents)
			{
				if (fileName.Equals(document.FullName, StringComparison.InvariantCultureIgnoreCase))
				{
					document.Close();
					break;
				}
			}
		}

		/// <summary>
		/// Moves the caret to line number.
		/// </summary>
		/// <param name="DTE">The DTE.</param>
		/// <param name="lineNumber">The line number.</param>
		internal static void GoToLine(EnvDTE.DTE DTE, int lineNumber)
		{
			DTE.ExecuteCommand("GotoLn", lineNumber.ToString());
		}

		/// <summary>
		/// Writes to the output window.
		/// </summary>
		/// <param name="message">The message.</param>
		internal static void WriteToOutputWindow(string message)
		{
			try
			{
				if (_pane != null)
				{
					_pane.OutputStringThreadSafe(message);
					_pane.OutputStringThreadSafe(Environment.NewLine);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(String.Format("{0}\n{1}", ex.Message, ex.StackTrace), Resources.MessageBoxTitle, MessageBoxButton.OK);
			}
		}

		/// <summary>
		/// Writes to output window.
		/// </summary>
		/// <param name="format">The string format.</param>
		/// <param name="arguments">The arguments to formatting.</param>
		internal static void WriteToOutputWindow(string format, params object[] arguments)
		{
			WriteToOutputWindow(String.Format(format, arguments));
		}


		/// <summary>
		/// Finds all the dlls in the project with reference to UnitTestFramework.dll
		/// </summary>
		/// <returns>List of all dlls which might contain tests</returns>
		/// <remarks>Customized to support xunit</remarks>
		internal static IEnumerable<string> GetPotentialTestDLLs()
		{
			string mstestPath = "Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll";
			string nunitPath = "nunit.Framework.dll";
		    string xunitPath = "xunit.dll"; // Path for xunit

			List<EnvDTE.Project> projects = new List<EnvDTE.Project>();

			GetProjects(DTE.Solution.Projects, projects);

			foreach (var currentProject in projects)
			{
				var vsProject2 = currentProject.Object as VSProject2;
				bool isTestProject = false;

				if (vsProject2 != null)
				{
					foreach (Reference reference in vsProject2.References)
					{
						var referenceFile = Path.GetFileName(reference.Path); //TODO: Must check xunitPath
						if (CheckReferenceFile(referenceFile, new []{mstestPath, nunitPath, xunitPath}))
						{
							isTestProject = true;
							break;
						}
					}

					if (isTestProject)
					{
						yield return GetOutputPath(currentProject);
					}
				}
			}

		}

	    private static bool CheckReferenceFile(string referenceFile, string[] paths)
	    {
	        foreach (var path in paths)
	        {
	            if (path.Equals(referenceFile, StringComparison.InvariantCultureIgnoreCase))
	                return true;
	        }

	        return false;
	    }

		private static void GetProjects(EnvDTE.Projects projects, List<EnvDTE.Project> projectList)
		{
			foreach (EnvDTE.Project project in projects)
				GetProjects(project, projectList);
		}

		private static void GetProjects(EnvDTE.Project project, List<EnvDTE.Project> projectList)
		{
			if (project == null)
				return;

			if (project.Kind.Contains("FAE04EC0-301F-11D3-BF4B-00C04F79EFBC"))
				projectList.Add(project);
			
			if (project.ProjectItems == null || project.ProjectItems.Count == 0)
				return;

			foreach (EnvDTE.ProjectItem proj in project.ProjectItems)
			{
				var DTEProject = proj.Object as EnvDTE.Project;
				if (DTEProject != null)
					GetProjects(DTEProject, projectList);
			}
		}

		internal static string GetImageURL(string url)
		{
			return String.Format("{0}{1}", BASE_IMAGE_PREFIX, url);
		}

		/// <summary>
		/// Returns the output path of the project.
		/// </summary>
		/// <param name="project">The project.</param>
		/// <returns>Output path</returns>
		internal static string GetOutputPath(EnvDTE.Project project)
		{
			string outputPath = project.ConfigurationManager != null && project.ConfigurationManager.ActiveConfiguration != null
				? project.ConfigurationManager.ActiveConfiguration.Properties.Item("OutputPath").Value.ToString() : null;

			if (outputPath == null)
			{
				return null;
			}

			string absoluteOutputPath;
			string projectFolder;

			if (outputPath.StartsWith(String.Format("{0}{0}", Path.DirectorySeparatorChar)))
			{
				// This is the case 1: "\\server\folder"
				absoluteOutputPath = outputPath;
			}
			else if (outputPath.Length >= 2 && outputPath[1] == Path.VolumeSeparatorChar)
			{
				// This is the case 2: "drive:\folder"
				absoluteOutputPath = outputPath;
			}
			else if (outputPath.IndexOf("..\\") != -1)
			{
				// This is the case 3: "..\..\folder"
				projectFolder = Path.GetDirectoryName(project.FullName);
				while (outputPath.StartsWith("..\\"))
				{
					outputPath = outputPath.Substring(3);
					projectFolder = Path.GetDirectoryName(projectFolder);
				}

				absoluteOutputPath = System.IO.Path.Combine(projectFolder, outputPath);
			}
			else
			{
				// This is the case 4: "folder"
				projectFolder = System.IO.Path.GetDirectoryName(project.FullName);
				absoluteOutputPath = System.IO.Path.Combine(projectFolder, outputPath);
			}

			return Path.Combine(absoluteOutputPath, project.Properties.Item("OutputFileName").Value.ToString());
		}

		internal static string GetIcon(TestExecutionStatus status)
		{
			string icon = "Resources/{0}";

			switch (status)
			{
				case TestExecutionStatus.NotRun:
					return string.Format(icon, "NotRun.png");
				case TestExecutionStatus.Successful:
					return string.Format(icon, "Successful.png");
				case TestExecutionStatus.Error:
					return string.Format(icon, "Failed.png");
				case TestExecutionStatus.Inconclusive:
					return string.Format(icon, "Inconclusive.png");
				default:
					return string.Format(icon, "NotRun.png");
			}
		}

		/// <summary>
		/// Returns the property value .
		/// </summary>
		/// <typeparam name="T">Generic Type for value of the property</typeparam>
		/// <param name="obj">The object.</param>
		/// <param name="propertyName">Name of the property.</param>
		/// <returns>Value of the property as T</returns>
		private static T GetPropertyValue<T>(Object obj, string propertyName) where T : class
		{
			return obj.GetType().GetProperty(propertyName).GetValue(obj) as T;
		}
	}
}
