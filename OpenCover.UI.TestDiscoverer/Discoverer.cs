﻿//
// This source code is released under the GPL License; Please read license.md file for more details.
//
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using NUnit.Framework;
using OpenCover.UI.Model.Test;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace OpenCover.UI.TestDiscoverer
{
	/// <summary>
	/// Discovers tests in the given dlls.
	/// </summary>
	internal class Discoverer
	{
		private IEnumerable<string> _dlls;

		/// <summary>
		/// Initializes a new instance of the <see cref="TestDiscoverer"/> class.
		/// </summary>
		/// <param name="dlls">The DLLS.</param>
		public Discoverer(IEnumerable<string> dlls)
		{
			_dlls = dlls;
		}

		/// <summary>
		/// Loads the assembly and all its referenced assemblies.
		/// </summary>
		/// <param name="dll">The DLL.</param>
		/// <returns>Loaded assembly</returns>
		private static AssemblyDefinition LoadAssembly(string dll)
		{
			//Assembly assembly = Assembly.ReflectionOnlyLoadFrom(dll);
			Directory.SetCurrentDirectory(Path.GetDirectoryName(dll));

			//Creates an AssemblyDefinition from the "MyLibrary.dll" assembly
			AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(dll);

			return assembly;
		}

		/// <summary>
		/// Discovers all tests in the selected assemblies.
		/// </summary>
		/// <returns></returns>
		public List<TestClass> Discover()
		{
			List<TestClass> tests = new List<TestClass>();

			if (_dlls != null)
			{
				foreach (var dll in _dlls)
				{
					tests.AddRange(DiscoverTestsInDLL(dll));
				}
			}

			return tests;
		}

		/// <summary>
		/// Discovers the tests in DLL.
		/// </summary>
		/// <param name="dll">The DLL.</param>
		/// <returns>Tests in the DLL</returns>
		private List<TestClass> DiscoverTestsInDLL(string dll)
		{
			var classes = new List<TestClass>();

			if (File.Exists(dll))
			{
				AssemblyDefinition assembly = null;
				try
				{
					assembly = LoadAssembly(dll);
				}
				catch { }

				if (assembly != null)
				{
					foreach (var type in assembly.MainModule.Types)
					{
						bool isMSTest = false;
						bool isNUnitTest = false;
					    bool isXUnitTest = false;

						try
						{
							var customAttributes = type.CustomAttributes;
							if (customAttributes != null)
							{
								isMSTest = customAttributes.Any(attribute => attribute.AttributeType.FullName == typeof(TestClassAttribute).FullName);
								isNUnitTest = IsNUnitTest(type);
							    isXUnitTest = IsXUnitTest(type);
							}
						}
						catch { }

						if (isMSTest || isNUnitTest || isXUnitTest)
						{
						    var testType = isNUnitTest ? TestType.NUnit : (isXUnitTest ? TestType.XUnit : TestType.MSTest);

							var TestClass = new TestClass
							{
								DLLPath = dll,
								Name = type.Name,
								Namespace = type.Namespace,
								TestType = testType
							};

							TestClass.TestMethods = DiscoverTestsInClass(type, TestClass, testType);
							classes.Add(TestClass);
						}
					}
				}
			}

			return classes;
		}

		/// <summary>
		/// Determines whether the Type has TestFixtrue Attribute on itself or on one of its parents
		/// </summary>
		/// <param name="type">The type.</param>
		private bool IsNUnitTest(TypeDefinition type)
		{
			if (type == null)
			{
				return false;
			}

			if (type.CustomAttributes != null && type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == typeof(TestFixtureAttribute).FullName))
			{
				return true;
			}

			if (type.BaseType != null && type.BaseType is TypeDefinition)
			{
				return IsNUnitTest(type.BaseType as TypeDefinition);
			}

			return false;
		}

        /// <summary>
        /// Determines whether the Type has FactAttribute on itself or on one of its parents
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        /// <remarks>Custom method for xunit</remarks>
	    private bool IsXUnitTest(TypeDefinition type)
	    {
            if (type == null)
            {
                return false;
            }

            if (type.CustomAttributes != null &&
                type.Methods.Any(
                    m =>
                        m.CustomAttributes.Any(
                            attribute => attribute.AttributeType.FullName == typeof (FactAttribute).FullName)))
            {
                return true;
            }

            if (type.BaseType != null && type.BaseType is TypeDefinition)
            {
                return IsNUnitTest(type.BaseType as TypeDefinition);
            }

            return false;
	    }

		/// <summary>
		/// Discovers the tests in class.
		/// </summary>
		/// <param name="type">Type of the class.</param>
		/// <returns>Tests in the class</returns>
		/// <remarks>Changed last param to test type so that more test types can be suported</remarks>
		private TestMethod[] DiscoverTestsInClass(TypeDefinition type, TestClass @class, TestType testType)
		{
			var tests = new List<TestMethod>();
			foreach (var method in type.Methods)
			{
				bool isTestMethod = false;
				var trait = new List<string>();

				try
				{
					foreach (var attribute in method.CustomAttributes)
					{
						if (testType == TestType.NUnit)
						{
							if (attribute.AttributeType.FullName == typeof(TestAttribute).FullName || attribute.AttributeType.FullName == typeof(TestCaseAttribute).FullName)
							{
								isTestMethod = true;
							}

							AddTraits(trait, attribute, typeof(CategoryAttribute));
						}
						else if (testType == TestType.MSTest)
						{
						    if (attribute.AttributeType.FullName == typeof (TestMethodAttribute).FullName)
						    {
						        isTestMethod = true;
						    }

						    AddTraits(trait, attribute, typeof (TestCategoryAttribute));
						}
						else
						{
                            if (attribute.AttributeType.FullName == typeof(FactAttribute).FullName)
                            {
                                isTestMethod = true;
                            }

                           //TODO: not sure what this should be in XUnit AddTraits(trait, attribute, typeof(TestCategoryAttribute));
						}
					}
				}
				catch { }

				if (isTestMethod)
				{
					TestMethod testMethod = new TestMethod();
					testMethod.Name = method.Name;
					testMethod.Traits = trait.Count > 0 ? trait.ToArray() : new[] { "No Traits" };
					tests.Add(testMethod);
				}
			}

			return tests.ToArray();
		}

		private static void AddTraits(List<string> trait, CustomAttribute attribute, Type attributeType)
		{
			if (attribute.AttributeType.FullName == attributeType.FullName)
			{
				if (attribute.ConstructorArguments != null && attribute.ConstructorArguments.Count > 0)
				{
					trait.Add(attribute.ConstructorArguments[0].Value.ToString());
				}
			}
		}
	}
}
