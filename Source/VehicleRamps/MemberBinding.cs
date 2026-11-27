using System;
using System.Linq;
using System.Reflection;

namespace AASBVehicleRamps;

/// <summary>
/// Startup checks for members this mod uses from Vehicle Framework, AASB and RimWorld. Each check throws an
/// <see cref="InvalidOperationException"/> naming the missing or mismatched member; the caller turns that into
/// one NOT ACTIVE error and installs no patches.
/// </summary>
public static class MemberBinding
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static Type RequireType(Type type, string name)
    {
        if (type == null)
        {
            throw new InvalidOperationException("missing type " + name);
        }

        return type;
    }

    /// <summary>Requires a field on the type or a base type with exactly the expected field type.</summary>
    public static FieldInfo RequireField(Type owner, string name, Type fieldType)
    {
        for (var current = owner; current != null; current = current.BaseType)
        {
            var field = current.GetField(name, Any);
            if (field == null)
            {
                continue;
            }

            if (field.FieldType != fieldType)
            {
                throw new InvalidOperationException("field " + owner.FullName + "." + name + " is "
                    + field.FieldType.FullName + ", expected " + fieldType.FullName);
            }

            return field;
        }

        throw new InvalidOperationException("missing field " + owner.FullName + "." + name);
    }

    /// <summary>
    /// Requires a method declared on the type (or a base type) with exact parameters and return type.
    /// </summary>
    public static MethodInfo RequireMethod(Type owner, string name, Type[] parameters, Type returnType)
    {
        for (var current = owner; current != null; current = current.BaseType)
        {
            var method = current.GetMethods(Any).FirstOrDefault(m => m.Name == name
                && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));
            if (method == null)
            {
                continue;
            }

            if (method.ReturnType != returnType)
            {
                throw new InvalidOperationException("method " + owner.FullName + "." + name + " returns "
                    + method.ReturnType.FullName + ", expected " + returnType.FullName);
            }

            return method;
        }

        throw new InvalidOperationException("missing method " + owner.FullName + "." + name + "("
            + string.Join(", ", parameters.Select(p => p.Name)) + ")");
    }

    /// <summary>Requires an instance constructor with exact parameters.</summary>
    public static ConstructorInfo RequireConstructor(Type owner, params Type[] parameters)
    {
        var constructor = owner.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, parameters, null);
        if (constructor == null)
        {
            throw new InvalidOperationException("missing constructor " + owner.FullName + "("
                + string.Join(", ", parameters.Select(p => p.Name)) + ")");
        }

        return constructor;
    }

    public static PropertyInfo RequireProperty(Type owner, string name, Type propertyType, bool needsSetter)
    {
        for (var current = owner; current != null; current = current.BaseType)
        {
            var property = current.GetProperty(name, Any);
            if (property == null)
            {
                continue;
            }

            if (property.PropertyType != propertyType || (needsSetter && property.SetMethod == null)
                || property.GetMethod == null)
            {
                throw new InvalidOperationException("property " + owner.FullName + "." + name
                    + " has an unexpected type or accessor");
            }

            return property;
        }

        throw new InvalidOperationException("missing property " + owner.FullName + "." + name);
    }
}
