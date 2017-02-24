﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Internal.Text;
using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler
{
    public class ImportExportOrdinals
    {
        public ReadOnlyDictionary<TypeDesc, uint> typeOrdinals;
        public ReadOnlyDictionary<MethodDesc, uint> methodOrdinals;
    }

    public class UTCNameMangler : NameMangler
    {
        private SHA256 _sha256;
        private string _compilationUnitPrefix;
        private string OrdinalPrefix => "_[O]_";
        private string deDuplicatePrefix => "_[D]_";
        private ImportExportOrdinals _importExportOrdinals;
        public bool OrdinalReceived { get; private set; }

        public UTCNameMangler(bool ordinalBased)
        {
            if (!ordinalBased)
            {
                OrdinalReceived = true;
                _importExportOrdinals = new ImportExportOrdinals();
                _importExportOrdinals.typeOrdinals = new ReadOnlyDictionary<TypeDesc, uint>(new Dictionary<TypeDesc, uint>());
                _importExportOrdinals.methodOrdinals = new ReadOnlyDictionary<MethodDesc, uint>(new Dictionary<MethodDesc, uint>());
            }
            else
            {
                OrdinalReceived = false;
            }            
        }

        public void ReceiveImportExportOrdinals(ImportExportOrdinals ordinals)
        {
            Debug.Assert(!OrdinalReceived);
            _importExportOrdinals = ordinals;
            OrdinalReceived = true;
        }

        public override string CompilationUnitPrefix
        {
            set { _compilationUnitPrefix = SanitizeNameWithHash(value); }
            get
            {
                System.Diagnostics.Debug.Assert(_compilationUnitPrefix != null);
                return _compilationUnitPrefix;
            }
        }

        //
        // Turn a name into a valid C/C++ identifier
        //
        internal override string SanitizeName(string s, bool typeName = false)
        {
            StringBuilder sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (((c >= 'a') && (c <= 'z')) || ((c >= 'A') && (c <= 'Z')))
                {
                    if (sb != null)
                        sb.Append(c);
                    continue;
                }

                if ((c >= '0') && (c <= '9'))
                {
                    // C identifiers cannot start with a digit. Prepend underscores.
                    if (i == 0)
                    {
                        if (sb == null)
                            sb = new StringBuilder(s.Length + 2);
                        sb.Append("_");
                    }
                    if (sb != null)
                        sb.Append(c);
                    continue;
                }

                if (sb == null)
                    sb = new StringBuilder(s, 0, i, s.Length);

                // For CppCodeGen, replace "." (C# namespace separator) with "::" (C++ namespace separator)
                if (typeName && c == '.')
                {
                    sb.Append("::");
                    continue;
                }

                // Everything else is replaced by underscore.
                // TODO: We assume that there won't be collisions with our own or C++ built-in identifiers.
                sb.Append("_");
            }

            string sanitizedName = (sb != null) ? sb.ToString() : s;

            // The character sequences denoting generic instantiations, arrays, byrefs, or pointers must be
            // restricted to that use only. Replace them if they happened to be used in any identifiers in 
            // the compilation input.
            return sanitizedName;
        }

        private string SanitizeNameWithHash(string literal)
        {
            string mangledName = SanitizeName(literal);

            if (mangledName.Length > 30)
                mangledName = mangledName.Substring(0, 30);

            if (mangledName != literal)
            {
                if (_sha256 == null)
                    _sha256 = SHA256.Create();

                var hash = _sha256.ComputeHash(Encoding.UTF8.GetBytes(literal));
                mangledName += "_" + BitConverter.ToString(hash).Replace("-", "");
            }

            return mangledName;
        }

        /// <summary>
        /// Dictionary given a mangled name for a given <see cref="TypeDesc"/>
        /// </summary>
        private ImmutableDictionary<TypeDesc, string> _mangledTypeNames = ImmutableDictionary<TypeDesc, string>.Empty;

        /// <summary>
        /// Given a set of names <param name="set"/> check if <param name="origName"/>
        /// is unique, if not add a numbered suffix until it becomes unique.
        /// </summary>
        /// <param name="origName">Name to check for uniqueness.</param>
        /// <param name="set">Set of names already used.</param>
        /// <returns>A name based on <param name="origName"/> that is not part of <param name="set"/>.</returns>
        private string DisambiguateName(string origName, ISet<string> set)
        {
            int iter = 0;
            string result = origName;
            while (set.Contains(result))
            {
                result = string.Concat(origName, deDuplicatePrefix, (iter++).ToStringInvariant());
            }
            return result;
        }

        public override string GetMangledTypeName(TypeDesc type)
        {
            string mangledName;
            if (_mangledTypeNames.TryGetValue(type, out mangledName))
                return mangledName;

            return ComputeMangledTypeName(type);
        }

        private string EnterNameScopeSequence => "<";
        private string ExitNameScopeSequence => ">";

        protected string NestMangledName(string name)
        {
            return EnterNameScopeSequence + name + ExitNameScopeSequence;
        }

        /// <summary>
        /// If given <param name="type"/> is an <see cref="EcmaType"/> precompute its mangled type name
        /// along with all the other types from the same module as <param name="type"/>.
        /// Otherwise, it is a constructed type and to the EcmaType's mangled name we add a suffix to
        /// show what kind of constructed type it is (e.g. appending __Array for an array type).
        /// </summary>
        /// <param name="type">Type to mangled</param>
        /// <returns>Mangled name for <param name="type"/>.</returns>
        private string ComputeMangledTypeName(TypeDesc type)
        {
            Debug.Assert(OrdinalReceived);

            if (type is EcmaType)
            {
                EcmaType ecmaType = (EcmaType)type;

                string prependAssemblyName = SanitizeName(((EcmaAssembly)ecmaType.EcmaModule).GetName().Name);

                var deduplicator = new HashSet<string>();

                // Add consistent names for all types in the module, independent on the order in which
                // they are compiled
                lock (this)
                {
                    if (!_mangledTypeNames.ContainsKey(type))
                    {
                        foreach (MetadataType t in ((EcmaType)type).EcmaModule.GetAllTypes())
                        {
                            string name = t.GetFullName();

                            // Include encapsulating type
                            DefType containingType = t.ContainingType;
                            while (containingType != null)
                            {
                                name = containingType.GetFullName() + "_" + name;
                                containingType = containingType.ContainingType;
                            }

                            name = SanitizeName(name, true);
                            name = prependAssemblyName + "_" + name;

                            // Ensure that name is unique and update our tables accordingly.
                            name = DisambiguateName(name, deduplicator);
                            deduplicator.Add(name);
                            _mangledTypeNames = _mangledTypeNames.Add(t, name);
                        }
                    }
                }

                return _mangledTypeNames[type];
            }

            string mangledName;

            switch (type.Category)
            {
                case TypeFlags.Array:
                case TypeFlags.SzArray:
                    mangledName = GetMangledTypeName(((ArrayType)type).ElementType) + "__";

                    if (type.IsMdArray)
                    {
                        mangledName += NestMangledName("ArrayRank" + ((ArrayType)type).Rank.ToStringInvariant());
                    }
                    else
                    {
                        mangledName += NestMangledName("Array");
                    }
                    break;
                case TypeFlags.ByRef:
                    mangledName = GetMangledTypeName(((ByRefType)type).ParameterType) + NestMangledName("ByRef");
                    break;
                case TypeFlags.Pointer:
                    mangledName = GetMangledTypeName(((PointerType)type).ParameterType) + NestMangledName("Pointer");
                    break;
                default:
                    // Case of a generic type. If `type' is a type definition we use the type name
                    // for mangling, otherwise we use the mangling of the type and its generic type
                    // parameters, e.g. A <B> becomes A_<___B_>_ in RyuJIT compilation, or A_A___B_V_
                    // in C++ compilation.
                    var typeDefinition = type.GetTypeDefinition();
                    if (typeDefinition != type)
                    {
                        mangledName = GetMangledTypeName(typeDefinition);

                        var inst = type.Instantiation;
                        string mangledInstantiation = "";
                        for (int i = 0; i < inst.Length; i++)
                        {
                            string instArgName = GetMangledTypeName(inst[i]);
                            if (i > 0)
                                mangledInstantiation += "__";

                            mangledInstantiation += instArgName;
                        }
                        mangledName += NestMangledName(mangledInstantiation);
                    }
                    else
                    {
                        mangledName = SanitizeName(((DefType)type).GetFullName(), true);
                    }
                    break;
            }

            lock (this)
            {
                // Ensure that name is unique and update our tables accordingly.
                if (!_mangledTypeNames.ContainsKey(type))
                    _mangledTypeNames = _mangledTypeNames.Add(type, mangledName);
            }

            return mangledName;
        }

        protected ImmutableDictionary<MethodDesc, Utf8String> _mangledMethodNames = ImmutableDictionary<MethodDesc, Utf8String>.Empty;

        public override Utf8String GetMangledMethodName(MethodDesc method)
        {
            Utf8String mangledName;
            if (_mangledMethodNames.TryGetValue(method, out mangledName))
                return mangledName;

            return ComputeMangledMethodName(method);
        }

        string RemoveDeduplicatePrefix(string name)
        {
            char lastChar = name[name.Length - 1];
            if ((lastChar >= '0') && (lastChar <= '9'))
            {
                int deDupPos = name.LastIndexOf(deDuplicatePrefix);
                if (deDupPos != -1)
                {
                    name = name.Substring(0, deDupPos);
                }
            }

            return name;
        }

        private Utf8String ComputeMangledNameMethodWithoutInstantiation(MethodDesc method)
        {
            Debug.Assert(method == method.GetMethodDefinition());

            string prependTypeName = GetMangledTypeName(method.OwningType);
            var deduplicator = new HashSet<string>();

            // Add consistent names for all methods of the type, independent on the order in which
            // they are compiled
            lock (this)
            {
                if (!_mangledMethodNames.ContainsKey(method))
                {
                    foreach (var m in method.OwningType.GetMethods())
                    {
                        string name = SanitizeName(m.Name);
                        uint ordinal;

                        // Ensure that name is unique and update our tables accordingly.
                        if (_importExportOrdinals.methodOrdinals.TryGetValue(m, out ordinal))
                        {
                            name += OrdinalPrefix + ordinal;
                        }
                        else
                        {
                            name = DisambiguateName(name, deduplicator);
                        }

                        Debug.Assert(!deduplicator.Contains(name));
                        deduplicator.Add(name);

                        if (prependTypeName != null)
                            name = prependTypeName + "__" + name;

                        _mangledMethodNames = _mangledMethodNames.Add(m, name);
                    }
                }
            }

            return _mangledMethodNames[method];
        }

        private Utf8String ComputeMangledMethodName(MethodDesc method)
        {
            Debug.Assert(OrdinalReceived);

            // Method is either a generic method instantiation or an instance method of a generic type instantiation
            var methodDefinition = method.GetMethodDefinition();
            Utf8String utf8MangledName = ComputeMangledNameMethodWithoutInstantiation(methodDefinition);

            // Append the instantiation vector for a generic method instantiation 
            if (methodDefinition != method)
            {
                string mangledInstantiation = "";
                var inst = method.Instantiation;
                for (int i = 0; i < inst.Length; i++)
                {
                    string instArgName = GetMangledTypeName(inst[i]);
                    if (i > 0)
                        mangledInstantiation += "__";
                    mangledInstantiation += instArgName;
                }

                mangledInstantiation = NestMangledName(mangledInstantiation);

                string mangledName = utf8MangledName.ToString();

                // Do not need the deDuplicator (which is not stable across builds) if the method has an importExport ordinal
                uint ordinal;
                if (_importExportOrdinals.methodOrdinals.TryGetValue(method, out ordinal))
                {
                    mangledName = RemoveDeduplicatePrefix(mangledName);
                    mangledName += mangledInstantiation;
                    mangledName += OrdinalPrefix + ordinal;
                }
                else
                {
                    mangledName += mangledInstantiation;
                }

                lock (this)
                {
                    utf8MangledName = new Utf8String(mangledName);
                    if (!_mangledMethodNames.ContainsKey(method))
                        _mangledMethodNames = _mangledMethodNames.Add(method, utf8MangledName);
                }
            }

            return utf8MangledName;
        }

        private ImmutableDictionary<FieldDesc, Utf8String> _mangledFieldNames = ImmutableDictionary<FieldDesc, Utf8String>.Empty;

        public override Utf8String GetMangledFieldName(FieldDesc field)
        {
            Utf8String mangledName;
            if (_mangledFieldNames.TryGetValue(field, out mangledName))
                return mangledName;

            return ComputeMangledFieldName(field);
        }

        private Utf8String ComputeMangledFieldName(FieldDesc field)
        {
            Debug.Assert(OrdinalReceived);

            string prependTypeName = null;
            prependTypeName = GetMangledTypeName(field.OwningType);

            if (field is EcmaField)
            {
                var deduplicator = new HashSet<string>();

                // Add consistent names for all fields of the type, independent on the order in which
                // they are compiled
                lock (this)
                {
                    if (!_mangledFieldNames.ContainsKey(field))
                    {
                        foreach (var f in field.OwningType.GetFields())
                        {
                            string name = SanitizeName(f.Name);

                            name = DisambiguateName(name, deduplicator);
                            deduplicator.Add(name);

                            if (prependTypeName != null)
                                name = prependTypeName + "__" + name;

                            _mangledFieldNames = _mangledFieldNames.Add(f, name);
                        }
                    }
                }

                return _mangledFieldNames[field];
            }


            string mangledName = SanitizeName(field.Name);

            if (prependTypeName != null)
                mangledName = prependTypeName + "__" + mangledName;

            Utf8String utf8MangledName = new Utf8String(mangledName);

            lock (this)
            {
                if (!_mangledFieldNames.ContainsKey(field))
                    _mangledFieldNames = _mangledFieldNames.Add(field, utf8MangledName);
            }

            return utf8MangledName;
        }

        private ImmutableDictionary<string, string> _mangledStringLiterals = ImmutableDictionary<string, string>.Empty;

        public override string GetMangledStringName(string literal)
        {
            Debug.Assert(OrdinalReceived);

            string mangledName;
            if (_mangledStringLiterals.TryGetValue(literal, out mangledName))
                return mangledName;

            mangledName = SanitizeNameWithHash(literal);

            lock (this)
            {
                if (!_mangledStringLiterals.ContainsKey(literal))
                    _mangledStringLiterals = _mangledStringLiterals.Add(literal, mangledName);
            }

            return mangledName;
        }
    }
}