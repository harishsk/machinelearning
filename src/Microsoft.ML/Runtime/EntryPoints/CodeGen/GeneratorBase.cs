// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.CSharp;
using Microsoft.ML.Runtime.CommandLine;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.Internal.Utilities;

namespace Microsoft.ML.Runtime.EntryPoints.CodeGen
{
    internal abstract class GeneratorBase
    {
        protected string Name;
        protected string Owner;
        protected string Version;
        protected string State;
        protected string ModuleType;
        protected string Determinism;
        protected string Category;
        protected HashSet<string> Exclude;
        protected HashSet<string> Namespaces;

        /// <summary>
        /// Generate the module and its implementation.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="prefix">The module prefix.</param>
        /// <param name="regenerate">The command string used to generate.</param>
        /// <param name="component">The component.</param>
        /// <param name="generateEnums">Whether to generate enums for SubComponents.</param>
        /// <param name="moduleId"></param>
        /// <param name="moduleName"></param>
        /// <param name="moduleOwner"></param>
        /// <param name="moduleVersion"></param>
        /// <param name="moduleState"></param>
        /// <param name="moduleType"></param>
        /// <param name="moduleDeterminism"></param>
        /// <param name="moduleCategory"></param>
        /// <param name="exclude">The set of parameters to exclude</param>
        /// <param name="namespaces">The set of extra namespaces</param>
        public void Generate(IndentingTextWriter writer, string prefix, string regenerate, ComponentCatalog.LoadableClassInfo component, bool generateEnums,
            string moduleId, string moduleName, string moduleOwner, string moduleVersion, string moduleState, string moduleType, string moduleDeterminism, string moduleCategory,
            HashSet<string> exclude, HashSet<string> namespaces)
        {
            Contracts.AssertValue(exclude);
            Name = moduleName;
            Owner = moduleOwner;
            Version = moduleVersion;
            State = moduleState;
            ModuleType = moduleType;
            Determinism = moduleDeterminism;
            Category = moduleCategory;
            Exclude = exclude;
            Namespaces = namespaces;

            GenerateHeader(writer, regenerate);
            using (writer.Nest())
            {
                GenerateClassName(writer, prefix, component);
                using (writer.Nest())
                    GenerateContent(writer, prefix, component, generateEnums, moduleId);
                GenerateFooter(writer);
            }
            GenerateFooter(writer);
        }

        private void GenerateHeader(IndentingTextWriter w, string regenerate)
        {
            w.WriteLine("//------------------------------------------------------------------------------");
            w.WriteLine("// <auto-generated>");
            w.WriteLine("//     This code was generated by a tool.");
            w.WriteLine("//");
            w.WriteLine("//     Changes to this file may cause incorrect behavior and will be lost if");
            w.WriteLine("//     the code is regenerated.");
            w.WriteLine("// </auto-generated>");
            w.WriteLine("//------------------------------------------------------------------------------");
            w.WriteLine("// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            w.WriteLine("// DO NOT MODIFY THIS FILE");
            w.WriteLine("// This file is generated from the TLC components, please don't modify.");
            w.WriteLine("// The following command was used to generate this file: " + regenerate);
            w.WriteLine("// Version used to generate this file: " + Assembly.GetExecutingAssembly().GetName().Version);
            w.WriteLine("// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            w.WriteLine();
            w.WriteLine("namespace Microsoft.Analytics.Platform.ML.IDVUtils");
            w.WriteLine("{");
            using (w.Nest())
                GenerateUsings(w);
        }

        protected abstract void GenerateUsings(IndentingTextWriter w);

        protected virtual void GenerateClassName(IndentingTextWriter w, string prefix, ComponentCatalog.LoadableClassInfo component)
        {
            w.WriteLine();
            var className = prefix + component.LoadNames[0];
            w.WriteLine("/// <summary>Module: {0}</summary>", className);
            w.WriteLine("public partial class {0}", className);
            w.WriteLine("{");
        }

        protected abstract void GenerateContent(IndentingTextWriter writer, string prefix, ComponentCatalog.LoadableClassInfo component, bool generateEnums, string moduleId);

        private void GenerateFooter(IndentingTextWriter w)
        {
            w.WriteLine("}");
        }

        protected virtual string EnumName(CmdParser.ArgInfo.Arg arg, Type sigType)
        {
            return Capitalize(arg.LongName) + ComponentCatalog.SignatureToString(sigType);
        }

        protected static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        protected bool IsColumnType(CmdParser.ArgInfo.Arg arg)
        {
            return
                typeof(OneToOneColumn).IsAssignableFrom(arg.ItemType) ||
                typeof(ManyToOneColumn).IsAssignableFrom(arg.ItemType) ||
                arg.ItemType == typeof(GenerateNumberTransform.Column);
        }

        protected bool IsStringColumnType(CmdParser.ArgInfo.Arg arg)
        {
            return arg.LongName == "column";
        }

        protected bool IsTrainer(Type sigType)
        {
            return
                sigType == typeof(SignatureTrainer) ||
                sigType == typeof(SignatureBinaryClassifierTrainer) ||
                sigType == typeof(SignatureMultiClassClassifierTrainer) ||
                sigType == typeof(SignatureRegressorTrainer) ||
                sigType == typeof(SignatureMultiOutputRegressorTrainer) ||
                sigType == typeof(SignatureRankerTrainer) ||
                sigType == typeof(SignatureAnomalyDetectorTrainer) ||
                sigType == typeof(SignatureClusteringTrainer) ||
                sigType == typeof(SignatureSequenceTrainer);
        }

        protected virtual void GenerateParameter(IndentingTextWriter w, string type, string name)
        {
            w.Write("{0} {1}", type, name);
        }

        protected virtual string GetCSharpTypeName(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                return GetCSharpTypeName(type.GetGenericArguments()[0]) + "?";

            string name;
            using (var p = new CSharpCodeProvider())
                name = p.GetTypeOutput(new CodeTypeReference(type));
            return name;
        }

        protected bool IsOptional(CmdParser.ArgInfo.Arg arg)
        {
            return
                arg.IsRequired && arg.DefaultValue != null ||
                arg.ItemType.IsGenericType && arg.ItemType.GetGenericTypeDefinition() == typeof(Nullable<>) ||
                arg.ItemType == typeof(string);
        }

        protected static string CastIfNeeded(CmdParser.ArgInfo.Arg arg)
        {
            return arg.ItemType == typeof(uint) ? "(uint)" : "";
        }

        protected void GenerateEnums(IndentingTextWriter w, ComponentCatalog.LoadableClassInfo component)
        {
            var argumentInfo = CmdParser.GetArgInfo(component.ArgType, component.CreateArguments());
            var seen = new HashSet<Tuple<Type, Type>>();
            foreach (var arg in argumentInfo.Args)
                GenerateEnums(w, arg, seen);
        }

        /// <summary>
        /// Generate enums for subcomponents. Uses ReflectionUtils to filter only the subcomponents that match the base type and the signature.
        /// </summary>
        /// <param name="w"></param>
        /// <param name="arg"></param>
        /// <param name="seen"></param>
        protected void GenerateEnums(IndentingTextWriter w, CmdParser.ArgInfo.Arg arg, HashSet<Tuple<Type, Type>> seen)
        {
            if (Exclude.Contains(arg.LongName))
                return;

            var moreEnums = new List<CmdParser.ArgInfo.Arg>();
            if (arg.IsHidden || !arg.IsSubComponentItemType)
                return;
            Contracts.Assert(arg.ItemType.GetGenericTypeDefinition() == typeof(SubComponent<,>));
            var types = arg.ItemType.GetGenericArguments();
            var baseType = types[0];
            var sigType = types[1];
            var key = new Tuple<Type, Type>(baseType, sigType);
            if (seen.Contains(key) || IsTrainer(sigType) || sigType == typeof(SignatureDataLoader))
                return;
            seen.Add(key);
            var typeName = EnumName(arg, sigType);
            w.WriteLine("/// <summary> Available choices for {0} </summary>", sigType);
            w.WriteLine("public enum {0}", typeName);
            w.Write("{");
            using (w.Nest())
            {
                var pre = "";
                if (arg.NullName != null)
                {
                    w.WriteLine();
                    GenerateEnumValue(w, null);
                    pre = ",";
                }
                var infos = ComponentCatalog.GetAllDerivedClasses(baseType, sigType);
                foreach (var info in infos)
                {
                    w.WriteLine(pre);
                    if (pre != "")
                        w.WriteLine();
                    pre = ",";
                    GenerateEnumValue(w, info);
                    var args = info.CreateArguments();
                    if (args == null)
                        continue;
                    var argInfo = CmdParser.GetArgInfo(args.GetType(), args);
                    moreEnums.AddRange(argInfo.Args);
                }
                w.WriteLine();
            }
            w.WriteLine("}");
            w.WriteLine();
            foreach (var argument in moreEnums)
                GenerateEnums(w, argument, seen);
        }

        protected abstract void GenerateEnumValue(IndentingTextWriter w, ComponentCatalog.LoadableClassInfo info);

        protected object Stringify(object value)
        {
            if (value == null)
                return null;
            var arr = value as Array;
            if (arr != null && arr.GetLength(0) > 0)
                return Stringify(arr.GetValue(0));
            var strval = value as string;
            if (strval != null)
            {
                if (strval == "")
                    return "string.Empty";
                return Quote(strval);
            }
            if (value is double)
                return ((double)value).ToString("R") + "d";
            if (value is float)
                return ((float)value).ToString("R") + "f";
            if (value is bool)
                return (bool)value ? "true" : "false";
            var type = value.GetType();
            if (type.IsEnum)
                return GetCSharpTypeName(type) + "." + value;
            return value;
        }

        private string Quote(string src)
        {
            var dst = src.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
            return "\"" + dst + "\"";
        }
    }
}