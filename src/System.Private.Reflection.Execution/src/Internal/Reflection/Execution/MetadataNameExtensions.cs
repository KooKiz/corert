// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Reflection;
using System.Collections.Generic;
using Debug = System.Diagnostics.Debug;

using global::Internal.Metadata.NativeFormat;

namespace Internal.Reflection.Execution
{
    internal static class MetadataNameExtentions
    {
        public static string GetFullName(this Handle handle, MetadataReader reader)
        {
            switch (handle.HandleType)
            {
                case HandleType.TypeDefinition:
                    return handle.ToTypeDefinitionHandle(reader).GetFullName(reader);
                case HandleType.TypeReference:
                    return handle.ToTypeReferenceHandle(reader).GetFullName(reader);

                case HandleType.NamespaceDefinition:
                    return handle.ToNamespaceDefinitionHandle(reader).GetFullName(reader);
                case HandleType.NamespaceReference:
                    return handle.ToNamespaceReferenceHandle(reader).GetFullName(reader);

                case HandleType.TypeSpecification:
                    return handle.ToTypeSpecificationHandle(reader).GetFullName(reader);
                case HandleType.TypeInstantiationSignature:
                    return handle.ToTypeInstantiationSignatureHandle(reader).GetFullName(reader);

                case HandleType.ArraySignature:
                    return handle.ToArraySignatureHandle(reader).GetFullName(reader);
                case HandleType.SZArraySignature:
                    return handle.ToSZArraySignatureHandle(reader).GetFullName(reader);

                case HandleType.PointerSignature:
                    return handle.ToPointerSignatureHandle(reader).GetFullName(reader);
                case HandleType.ByReferenceSignature:
                    return handle.ToByReferenceSignatureHandle(reader).GetFullName(reader);
            }
            return null;
        }

        public static string GetFullName(this ByReferenceSignatureHandle handle, MetadataReader reader)
        {
            var result = handle.GetByReferenceSignature(reader).Type.GetFullName(reader);
            if (result == null) return null;
            return result + "&";
        }

        public static string GetFullName(this PointerSignatureHandle handle, MetadataReader reader)
        {
            var result = handle.GetPointerSignature(reader).Type.GetFullName(reader);
            if (result == null) return null;
            return result + "*";
        }

        public static string GetFullName(this ArraySignatureHandle handle, MetadataReader reader)
        {
            ArraySignature array = handle.GetArraySignature(reader);
            var result = array.ElementType.GetFullName(reader);
            if (result == null) return null;
            return result + "[" + (new string(',', array.Rank - 1)) + "]";
        }

        public static string GetFullName(this SZArraySignatureHandle handle, MetadataReader reader)
        {
            var result = handle.GetSZArraySignature(reader).ElementType.GetFullName(reader);
            if (result == null) return null;
            return result + "[]";
        }

        public static string GetFullName(this TypeSpecificationHandle typeSpecHandle, MetadataReader reader)
        {
            var typeSpec = typeSpecHandle.GetTypeSpecification(reader);

            if (typeSpec.Signature.IsNull(reader))
                return null;

            return typeSpec.Signature.GetFullName(reader);
        }

        public static string GetFullName(this TypeInstantiationSignatureHandle typeInstSigHandle, MetadataReader reader)
        {
            var typeInstSig = typeInstSigHandle.GetTypeInstantiationSignature(reader);

            if (typeInstSig.GenericType.IsNull(reader))
                return null;

            var name = typeInstSig.GenericType.GetFullName(reader);
            if (name == null)
                return null;

            var index = 0;
            string argsString = null;
            foreach (var argHandle in typeInstSig.GenericTypeArguments)
            {
                if (index > 0) argsString += ",";
                var argName = argHandle.GetFullName(reader);
                if (argName == null) return name;
                argsString += argName;
                index++;
            }
            return name + "<" + argsString + ">";
        }

        public static string GetFullName(this TypeDefinitionHandle typeDefHandle, MetadataReader reader)
        {
            var typeDef = typeDefHandle.GetTypeDefinition(reader);

            Debug.Assert(!typeDef.Name.IsNull(reader));

            var name = typeDef.Name.GetConstantStringValue(reader).Value;
            var enclosing = typeDef.EnclosingType.IsNull(reader) ? null : typeDef.EnclosingType.GetFullName(reader);
            var nspace = typeDef.NamespaceDefinition.IsNull(reader) ? null : typeDef.NamespaceDefinition.GetFullName(reader);

            if (enclosing != null && name != null)
                return enclosing + "+" + name;
            else if (nspace != null && name != null)
                return nspace + "." + name;

            return name;
        }

        public static string GetFullName(this NamespaceDefinitionHandle namespaceHandle, MetadataReader reader)
        {
            var nspace = namespaceHandle.GetNamespaceDefinition(reader);

            if (nspace.Name.IsNull(reader))
                return null;

            var name = nspace.Name.GetConstantStringValue(reader).Value;
            var containingNamespace = nspace.ParentScopeOrNamespace.IsNull(reader) ? null : nspace.ParentScopeOrNamespace.GetFullName(reader);

            if (containingNamespace != null)
                return containingNamespace + "." + name;

            return name;
        }

        public static string GetFullName(this TypeReferenceHandle typeRefHandle, MetadataReader reader)
        {
            var typeRef = typeRefHandle.GetTypeReference(reader);

            Debug.Assert(!typeRef.TypeName.IsNull(reader));

            var name = typeRef.TypeName.GetConstantStringValue(reader).Value;
            var enclosing = typeRef.ParentNamespaceOrType.HandleType == HandleType.TypeReference ? typeRef.ParentNamespaceOrType.GetFullName(reader) : null;
            var nspace = typeRef.ParentNamespaceOrType.HandleType == HandleType.NamespaceReference ? typeRef.ParentNamespaceOrType.GetFullName(reader) : null;

            if (enclosing != null && name != null)
                return enclosing + "+" + name;
            else if (nspace != null && name != null)
                return nspace + "." + name;

            return name;
        }

        public static string GetFullName(this NamespaceReferenceHandle namespaceHandle, MetadataReader reader)
        {
            var nspace = namespaceHandle.GetNamespaceReference(reader);

            if (nspace.Name.IsNull(reader))
                return null;

            var name = nspace.Name.GetConstantStringValue(reader).Value;
            var containingNamespace = nspace.ParentScopeOrNamespace.IsNull(reader) ? null : nspace.ParentScopeOrNamespace.GetFullName(reader);

            if (containingNamespace != null)
                return containingNamespace + "." + name;

            return name;
        }
    }
}
