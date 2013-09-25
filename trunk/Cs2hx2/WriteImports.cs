﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cs2hx.Translations;
using Roslyn.Compilers.CSharp;

namespace Cs2hx
{
	public static class WriteImports
	{

		/// <summary>
		/// Anything that we need always available, even if nothing in user code references it.
		/// </summary>
		static public string StandardImports = @"import system.Cs2Hx;
import system.Exception;";

		/// <summary>
		/// TODO: Calculate these by parsing our system haxe dir
		/// </summary>
		static public string[] SystemImports = new[] { 
"system.ArgumentException",
"system.collections.generic.CSDictionary",
"system.collections.generic.HashSet",
"system.collections.generic.KeyValuePair",
"system.DateTime",
"system.diagnostics.Stopwatch",
"system.Enumerable",
"system.Exception",
"system.Guid",
"system.IDisposable",
"system.InvalidOperationException",
"system.io.BinaryReader",
"system.io.BinaryWriter",
"system.KeyNotFoundException",
"system.linq.Linq",
"system.NotImplementedException",
"system.Nullable_Float",
"system.Nullable_Int",
"system.Nullable_Bool",
"system.Nullable_TimeSpan",
"system.Nullable_DateTime",
"system.OverflowException",
"system.RandomAS",
"system.text.StringBuilder",
"system.text.UTF8Encoding",
"system.ThreadAbortException",
"system.TimeoutException",
"system.TimeSpan",
"system.Environment",
"system.xml.linq.XAttribute",
"system.xml.linq.XElement",
"system.xml.linq.XContainer",
"system.xml.linq.XDocument",
"system.xml.linq.XObject",
"haxe.io.Bytes"
        };

		public static void Go(HaxeWriter writer)
		{
			var partials = TypeState.Instance.Partials;

			//Write import statements.  First, all StandardImports are always considered
			var imports = SystemImports.ToList();

			//Also allow users to specify extra import statements in the xml file
			imports.AddRange(Translations.Translation.ExtraImports());

			//Add in imports from the C#'s using statements
			foreach (var usingDeclaration in
				partials.SelectMany(o => o.Parent.ChildNodes().OfType<UsingDirectiveSyntax>())
				.Concat(partials.SelectMany(o => o.Parent.Parent.ChildNodes().OfType<UsingDirectiveSyntax>()))
				.Select(o => o.Name.ToString())
				.Distinct()
				.OrderBy(o => o))
			{
				if (usingDeclaration.StartsWith("System.") || usingDeclaration == "System")
					continue; //system usings are handled by our standard imports

				imports.AddRange(TypeState.Instance.GetTypesInNamespace(usingDeclaration).Select(t => usingDeclaration.ToLower() + "." + t.Identifier.ValueText));
			}

			//Filter out any ones that aren't being used by this file
			imports = FilterUnusedImports(imports, partials);

			//Cs2hx is always present, since we can't easily determine if it should be filtered
			writer.WriteLine(StandardImports);

			//Write the imports
			foreach (var import in imports.OrderBy(o => o))
				writer.WriteLine("import " + import + ";");


		}

		/// <summary>
		/// Filters out import statements that we know aren't needed.
		/// This algorithm isn't perfect, and in some edge cases will leave extra import statements that aren't needed.  These don't cause any problems, though, they just look ugly.
		/// </summary>
		private static List<string> FilterUnusedImports(List<string> imports, IEnumerable<TypeDeclarationSyntax> partials)
		{
			var allNodes = partials.SelectMany(classType => classType.DescendantNodesAndSelf());
			var typesReferenced = allNodes.OfType<TypeSyntax>()
				.Select(o => TypeProcessor.TryConvertType(o))
				.Where(o => o != null)
				.SelectMany(SplitGenericTypes)
				//.Concat(typeObjects.Select(o => o.Type))
				.ToHashSet(false);

			//Add in static references.  Any MemberAccess where the expression is a simple identifier means it's the root of a static call
			foreach (var symbol in allNodes.OfType<MemberAccessExpressionSyntax>()
				.Select(o => o.Expression)
				.OfType<IdentifierNameSyntax>()
				.Select(o => TypeState.Instance.GetModel(o).GetSymbolInfo(o).Symbol)
				.OfType<NamedTypeSymbol>()
				.Where(o => o.Kind == SymbolKind.NamedType))
				typesReferenced.Add(symbol.Name);
				
			//Add in extension methods.
			foreach (var symbol in allNodes.OfType<InvocationExpressionSyntax>()
				.Select(o => TypeState.Instance.GetModel(o).GetSymbolInfo(o).Symbol.As<MethodSymbol>().UnReduce())
				.Where(o => o.IsExtensionMethod))
				typesReferenced.Add(Translation.ExtensionName(symbol.ContainingType));
			
				

			return imports.Where(o => typesReferenced.Contains(o.SubstringAfterLast('.'))).Distinct().ToList();
		}


		static string[] GenericTokens = new string[] { "->", "(", ")", "<", ">", " " };

		private static List<string> SplitGenericTypes(string typeString)
		{
			int readerIndex = 0;

			Func<char, bool> isLiteralChar = c => char.IsLetterOrDigit(c) || c == '_';

			Func<string> readToken = () =>
			{
				var sb = new StringBuilder();
				var first = typeString[readerIndex++];
				sb.Append(first.ToString());
				while (readerIndex < typeString.Length)
				{
					var c = typeString[readerIndex];

					if (isLiteralChar(c) != isLiteralChar(first))
						return sb.ToString().Trim();

					sb.Append(c.ToString());
					readerIndex++;
				}
				return sb.ToString().Trim();
			};

			var ret = new List<string>();

			while (readerIndex < typeString.Length)
			{
				var token = readToken();

				if (token.Length == 0 || token == "." || token == ",")
					continue;

				if (!GenericTokens.Contains(token))
					ret.Add(token);
			}

			return ret;
		}


	}
}
