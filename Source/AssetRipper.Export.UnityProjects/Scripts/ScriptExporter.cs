﻿using AsmResolver.DotNet;
using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Export;
using AssetRipper.Export.UnityProjects.Configuration;
using AssetRipper.Export.UnityProjects.Project.Exporters;
using AssetRipper.Export.UnityProjects.Scripts.AssemblyDefinitions;
using AssetRipper.Import.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.Utils;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using System.Diagnostics;

namespace AssetRipper.Export.UnityProjects.Scripts
{
	public class ScriptExporter : IAssetExporter
	{
		public ScriptExporter(IAssemblyManager assemblyManager, LibraryConfiguration configuration)
		{
			AssemblyManager = assemblyManager;
			Decompiler = new ScriptDecompiler(AssemblyManager);
			LanguageVersion = configuration.ScriptLanguageVersion.ToCSharpLanguageVersion(configuration.Version);
			ScriptContentLevel = configuration.ScriptContentLevel;
		}

		public IAssemblyManager AssemblyManager { get; }
		private ICSharpCode.Decompiler.CSharp.LanguageVersion LanguageVersion { get; }
		private ScriptContentLevel ScriptContentLevel { get; }
		private ScriptDecompiler Decompiler { get; set; }

		public bool IsHandle(IUnityObjectBase asset) => asset is IMonoScript;

		public IExportCollection CreateCollection(TemporaryAssetCollection virtualFile, IUnityObjectBase asset)
		{
			return new ScriptExportCollection(this, (IMonoScript)asset);
		}

		public bool Export(IExportContainer container, IUnityObjectBase asset, string path)
		{
			throw new NotSupportedException("Need to export all scripts at once");
		}

		public void Export(IExportContainer container, IUnityObjectBase asset, string path, Action<IExportContainer, IUnityObjectBase, string> callback)
		{
			throw new NotSupportedException("Need to export all scripts at once");
		}

		public bool Export(IExportContainer container, IEnumerable<IUnityObjectBase> assets, string dirPath)
		{
			Export(container, assets, dirPath, null);
			return true;
		}

		public AssetType ToExportType(IUnityObjectBase asset) => AssetType.Meta;

		public bool ToUnknownExportType(Type type, out AssetType assetType)
		{
			assetType = AssetType.Meta;
			return true;
		}

		public void Export(IExportContainer container, IEnumerable<IUnityObjectBase> assets, string assetsDirectoryPath, Action<IExportContainer, IUnityObjectBase, string>? callback)
		{
			Logger.Info(LogCategory.Export, "Exporting scripts...");
			ArgumentException.ThrowIfNullOrEmpty(assetsDirectoryPath, nameof(assetsDirectoryPath));

			Dictionary<string, AssemblyDefinitionDetails> assemblyDefinitionDetailsDictionary = new();

			if (AssemblyManager.IsSet)
			{
				Decompiler.LanguageVersion = LanguageVersion;
				Decompiler.ScriptContentLevel = ScriptContentLevel;

				foreach (AssemblyDefinition assembly in AssemblyManager.GetAssemblies())
				{
					string assemblyName = assembly.Name!;
					if (ReferenceAssemblies.IsReferenceAssembly(assemblyName))
					{
						continue;
					}

					Logger.Info(LogCategory.Export, $"Decompiling {assemblyName}");
					string outputDirectory = Path.Combine(assetsDirectoryPath, GetScriptsFolderName(assemblyName), assemblyName);
					Directory.CreateDirectory(outputDirectory);
					Decompiler.DecompileWholeProject(assembly, outputDirectory);

					assemblyDefinitionDetailsDictionary.TryAdd(assemblyName, new AssemblyDefinitionDetails(assembly, outputDirectory));
				}
			}

			foreach (IMonoScript asset in assets.Cast<IMonoScript>())
			{
				if (ScriptExportCollection.IsEngineScript(asset))
				{
					continue;
				}

				GetExportSubPath(asset, out string subFolderPath, out string fileName);
				string folderPath = Path.Combine(assetsDirectoryPath, subFolderPath);
				string filePath = Path.Combine(folderPath, fileName);
				if (!File.Exists(filePath))
				{
					Directory.CreateDirectory(folderPath);
					File.WriteAllText(filePath, GetEmptyScriptContent(asset));
					string assemblyName = BaseManager.ToAssemblyName(asset.GetAssemblyNameFixed());
					if (!assemblyDefinitionDetailsDictionary.ContainsKey(assemblyName))
					{
						Debug.Assert(GetScriptsFolderName(assemblyName) is "Scripts");
						string assemblyDirectoryPath = Path.Combine(assetsDirectoryPath, "Scripts", assemblyName);
						AssemblyDefinitionDetails details = new AssemblyDefinitionDetails(assemblyName, assemblyDirectoryPath);
						assemblyDefinitionDetailsDictionary.Add(assemblyName, details);
					}
				}

				if (callback is not null)
				{
					if (File.Exists($"{filePath}.meta"))
					{
						Logger.Error(LogCategory.Export, $"Metafile already exists at {filePath}.meta");
						//throw new Exception($"Metafile already exists at {filePath}.meta");
					}
					else
					{
						callback.Invoke(container, asset, filePath);
					}
				}
			}

			// assembly definitions were added in 2017.3
			//     see: https://blog.unity.com/technology/unity-2017-3b-feature-preview-assembly-definition-files-and-transform-tool
			if (assemblyDefinitionDetailsDictionary.Count > 0 && container.ExportVersion.IsGreaterEqual(2017, 3))
			{
				foreach (AssemblyDefinitionDetails details in assemblyDefinitionDetailsDictionary.Values)
				{
					// exclude predefined assemblies like Assembly-CSharp.dll
					//    see: https://docs.unity3d.com/2017.3/Documentation/Manual/ScriptCompilationAssemblyDefinitionFiles.html
					if (!ReferenceAssemblies.IsPredefinedAssembly(details.AssemblyName))
					{
						AssemblyDefinitionExporter.Export(details);
					}
				}
			}
		}

		private static string GetScriptsFolderName(string assemblyName)
		{
			return assemblyName is "Assembly-CSharp-firstpass" or "Assembly - CSharp - firstpass" ? "Plugins" : "Scripts";
		}

		private static void GetExportSubPath(string assembly, string @namespace, string @class, out string folderPath, out string fileName)
		{
			string assemblyFolder = BaseManager.ToAssemblyName(assembly);
			string scriptsFolder = GetScriptsFolderName(assemblyFolder);
			string namespaceFolder = @namespace.Replace('.', Path.DirectorySeparatorChar);
			folderPath = DirectoryUtils.FixInvalidPathCharacters(Path.Combine(scriptsFolder, assemblyFolder, namespaceFolder));
			fileName = $"{DirectoryUtils.FixInvalidPathCharacters(@class)}.cs";
		}

		private static void GetExportSubPath(IMonoScript script, out string folderPath, out string fileName)
		{
			GetExportSubPath(script.GetAssemblyNameFixed(), script.Namespace_C115.String, script.ClassName_C115.String, out folderPath, out fileName);
		}

		private static string GetEmptyScriptContent(IMonoScript script)
		{
			return GetEmptyScriptContent(script.Namespace_C115.String, script.ClassName_C115.String);
		}

		private static string GetEmptyScriptContent(string? @namespace, string name)
		{
			if (string.IsNullOrEmpty(@namespace))
			{
				return $$"""
					using UnityEngine;

					public class {{name}} : MonoBehaviour
					{
						//Dummy class. Use different settings or provide .NET dll files for better decompilation output
					}
					""";
			}
			else
			{
				return $$"""
					using UnityEngine;

					namespace {{@namespace}}
					{
						public class {{name}} : MonoBehaviour
						{
							//Dummy class. Use different settings or provide .NET dll files for better decompilation output
						}
					}
					""";
			}
		}
	}
}
