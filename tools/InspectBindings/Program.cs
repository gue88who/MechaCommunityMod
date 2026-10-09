using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();
foreach (var h in md.TypeDefinitions)
{
    var t = md.GetTypeDefinition(h); var name = md.GetString(t.Name);
    var methodSearch = args.Contains("--methods");
    if (methodSearch) {
        foreach (var handle in t.GetMethods()) {
            var method = md.GetMethodDefinition(handle); var methodName = md.GetString(method.Name);
            if (!args.Skip(1).Where(s => s != "--methods").Any(s => methodName.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            var signature = method.DecodeSignature(new Types(), null);
            Console.WriteLine(md.GetString(t.Namespace) + "." + name + " :: " + signature.ReturnType + " " + methodName + "(" + string.Join(", ", signature.ParameterTypes) + ")");
        }
        continue;
    }
    if (!args.Skip(1).Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
    Console.WriteLine(md.GetString(t.Namespace) + "." + name);
    if (t.BaseType.Kind == HandleKind.TypeDefinition) Console.WriteLine("  base " + md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)t.BaseType).Name));
    if (t.BaseType.Kind == HandleKind.TypeReference) Console.WriteLine("  base " + md.GetString(md.GetTypeReference((TypeReferenceHandle)t.BaseType).Name));
    if (t.BaseType.Kind == HandleKind.TypeSpecification) Console.WriteLine("  base " + md.GetTypeSpecification((TypeSpecificationHandle)t.BaseType).DecodeSignature(new Types(), null));
    foreach (var f in t.GetFields()) {
        var field = md.GetFieldDefinition(f);
        if ((field.Attributes & System.Reflection.FieldAttributes.Literal) != 0) Console.WriteLine("  constant " + md.GetString(field.Name));
    }
    foreach (var p in t.GetProperties()) {
        var property = md.GetPropertyDefinition(p);
        Console.WriteLine("  property " + md.GetString(property.Name) + " : " + property.DecodeSignature(new Types(), null).ReturnType);
    }
    foreach (var m in t.GetMethods()) {
        var method = md.GetMethodDefinition(m); var n = md.GetString(method.Name);
        if (n.StartsWith("get_") || n.StartsWith("set_") || n.StartsWith(".cctor")) continue;
        var sig = method.DecodeSignature(new Types(), null);
        Console.WriteLine("  " + sig.ReturnType + " " + n + "(" + string.Join(", ", sig.ParameterTypes) + ") " + string.Join(", ", method.GetParameters().Select(x => md.GetString(md.GetParameter(x).Name))));
    }
}
class Types : ISignatureTypeProvider<string, object> {
 public string GetArrayType(string e, ArrayShape s) => e + "[]";
 public string GetByReferenceType(string e) => e + "&";
 public string GetFunctionPointerType(MethodSignature<string> s) => "fn";
 public string GetGenericInstantiation(string t, System.Collections.Immutable.ImmutableArray<string> a) => t + "<" + string.Join(",", a) + ">";
 public string GetGenericMethodParameter(object c,int i) => "M"+i;
 public string GetGenericTypeParameter(object c,int i) => "T"+i;
 public string GetModifiedType(string m,string u,bool r) => u;
 public string GetPinnedType(string e) => e;
 public string GetPointerType(string e) => e+"*";
 public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
 public string GetSZArrayType(string e) => e+"[]";
 public string GetTypeFromDefinition(MetadataReader r,TypeDefinitionHandle h,byte k) => r.GetString(r.GetTypeDefinition(h).Name);
 public string GetTypeFromReference(MetadataReader r,TypeReferenceHandle h,byte k) => r.GetString(r.GetTypeReference(h).Name);
 public string GetTypeFromSpecification(MetadataReader r,object c,TypeSpecificationHandle h,byte k) => r.GetTypeSpecification(h).DecodeSignature(this,c);
}
