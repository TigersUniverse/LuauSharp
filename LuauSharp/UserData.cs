using System.ComponentModel;
using System.Dynamic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LuauSharp;

public unsafe class UserData : IDisposable
{
    private Luau.lua_State* state;
    internal List<GCHandle> typeHandles = new();
    private static Dictionary<(Type, ExpandoObject), Dictionary<string, int>> staticClassCache = new();

    internal UserData(Luau.lua_State* state) => this.state = state;

    public static bool IsTypeAllowed(Type t) => staticClassCache.Count(x => x.Key.Item1 == t) > 0;

    private static Dictionary<string, int> GetStaticCacheFromExpandoObject(ExpandoObject expandoObject)
    {
        foreach (KeyValuePair<(Type, ExpandoObject),Dictionary<string,int>> keyValuePair in staticClassCache)
        {
            if(keyValuePair.Key.Item2 != expandoObject) continue;
            return keyValuePair.Value;
        }
        throw new Exception("Static Cache not found for " + expandoObject);
    }
    
    private static string? ReadString(Luau.lua_State* state, int index)
    {
        IntPtr lengthPointer;
        IntPtr stringPointer = Luau.lua_tolstring(state, index, out lengthPointer);
        if(stringPointer == IntPtr.Zero) return null;
        int length = lengthPointer.ToInt32();
        return Marshal.PtrToStringAnsi(stringPointer, length);
    }
    
    public string? ReadString(int index)
    {
        IntPtr lengthPointer;
        IntPtr stringPointer = Luau.lua_tolstring(state, index, out lengthPointer);
        if(stringPointer == IntPtr.Zero) return null;
        int length = lengthPointer.ToInt32();
        return Marshal.PtrToStringAnsi(stringPointer, length);
    }
    
    private static int? ReadInteger(Luau.lua_State* state, int index)
    {
        int isnum;
        int result = Luau.lua_tointegerx(state, index, out isnum);
        if (isnum == 0) return null;
        return result;
    }
    
    public int? ReadInteger(int index)
    {
        int isnum;
        int result = Luau.lua_tointegerx(state, index, out isnum);
        if (isnum == 0) return null;
        return result;
    }
    
    private static double? ReadNumber(Luau.lua_State* state, int index)
    {
        int isnum;
        double result = Luau.lua_tonumberx(state, index, out isnum);
        if (isnum == 0) return null;
        return result;
    }
    
    public double? ReadNumber(int index)
    {
        int isnum;
        double result = Luau.lua_tonumberx(state, index, out isnum);
        if (isnum == 0) return null;
        return result;
    }
    
    private static bool ReadBoolean(Luau.lua_State* state, int index) => Luau.lua_toboolean(state, index) == 1;

    public bool ReadBoolean(int index) => Luau.lua_toboolean(state, index) == 1;

    public T? ReadLightUserDataTagged<T>(TypeConverter typeConverter, int index, int tag)
    {
        IntPtr userDataPtr = Luau.lua_tolightuserdatatagged(state, index, tag);
        if (userDataPtr == IntPtr.Zero) return default;
        GCHandle handle = GCHandle.FromIntPtr(userDataPtr);
        return (T) typeConverter.ConvertFrom(handle.Target);
    }

    
    private static object ReadFunction(Luau.lua_State* luaState, int index)
    {
        IntPtr luaFuncPtr = Luau.lua_tocfunction(luaState, index);
        if (luaFuncPtr != IntPtr.Zero)
        {
            return (Luau.LuaCFunction)Marshal.GetDelegateForFunctionPointer(luaFuncPtr, typeof(Luau.LuaCFunction));
        }
        return new LuaFunctionWrapper(luaState, index);
    }
    
    public Delegate? ReadFunction(int index)
    {
        IntPtr userDataPtr = Luau.lua_tocfunction(state, index);
        if (userDataPtr == IntPtr.Zero) return null;
        GCHandle handle = GCHandle.FromIntPtr(userDataPtr);
        return (Delegate) handle.Target;
    }
    
    private void _PushFunction(Luau.LuaCFunction luaCFunction, Luau.LuaContinuation luaContinuation, string name,
        string debugName)
    {
        Luau.lua_pushcclosurek(state, luaCFunction, string.IsNullOrEmpty(debugName) ? name + "-debug" : debugName,
            0, luaContinuation);
        Luau.lua_setfield(state, Luau.LUA_GLOBALSINDEX, name);
    }

    public void PushFunction(string name, Delegate d)
    {
        Luau.LuaCFunction luaFunction = (luaState) =>
        {
            var delegateParams = d.Method.GetParameters();
            object?[] args = new object?[delegateParams.Length];
            for (int i = 0; i < delegateParams.Length; i++)
                args[i] = GetLuaValue(luaState, i + 1);
            object? result = d.DynamicInvoke(args);
            if (d.Method.ReturnType != typeof(void))
            {
                PushObjectToLua(result, d.Method.ReturnType);
                return 1;
            }
            return 0;
        };
        Luau.lua_pushcclosurek(state, luaFunction, name, 0, (_, _) => 0);
        Luau.lua_setfield(state, Luau.LUA_GLOBALSINDEX, name);
    }

    private void RewriteValues(ref object[] values, ParameterInfo[] parameters)
    {
        for (int i = 0; i < values.Length; i++)
        {
            object value = values[i];
            ParameterInfo parameterInfo = parameters[i];
            if (parameterInfo.ParameterType.IsEnum)
            {
                values[i] = Convert.ChangeType(value, TypeCode.Int32);
                continue;
            }
            if(!parameterInfo.ParameterType.IsPrimitive) continue;
            values[i] = Convert.ChangeType(value, parameterInfo.ParameterType);
        }
    }
    
    public void ForwardType<T>()
    {
        Type type = typeof(T);
        dynamic staticContainer = new ExpandoObject();
        Dictionary<string, int> staticCache = new Dictionary<string, int>();
        FieldInfo[] fields = type.GetFields(BindingFlags.Static | BindingFlags.Public);
        foreach (FieldInfo field in fields)
        {
            object? fieldValue = field.GetValue(null);
            ((IDictionary<string, object>)staticContainer)[field.Name] = new Func<bool, object, object?>((isRead, value) =>
            {
                if (isRead)
                    return field.GetValue(null);
                if (field.IsInitOnly)
                    throw new Exception("Cannot write to read only Field!");
                field.SetValue(null, value);
                return null;
            });
            staticCache.Add(field.Name, 0);
        }
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Static | BindingFlags.Public);
        foreach (PropertyInfo property in properties)
        {
            ((IDictionary<string, object>)staticContainer)[property.Name] = new Func<bool, object, object?>((isRead, value) =>
            {
                if (isRead)
                    return property.GetValue(null);
                if (!property.CanWrite)
                    throw new Exception("Cannot write to read only Property!");
                property.SetValue(null, value);
                return null;
            });
            staticCache.Add(property.Name, 1);
        }
        MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public);
        foreach (MethodInfo method in methods)
        {
            ((IDictionary<string, object>)staticContainer)[method.Name] = new Func<bool, object[], object?>((isRead, args) =>
            {
                RewriteValues(ref args, method.GetParameters());
                object? result = method.Invoke(null, args);
                return result;
            });
            staticCache.Add(method.Name, 2);
        }
        ConstructorInfo[] constructorInfos = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        if (constructorInfos.Length > 0)
        {
            ((IDictionary<string, object>)staticContainer)["new"] = new Func<bool, object[], T>((isRead, args) =>
            {
                foreach (var constructorInfo in constructorInfos)
                {
                    ParameterInfo[] parameters = constructorInfo.GetParameters();
                    if (parameters.Length == args.Length)
                    {
                        object?[] valueParameters = new object?[parameters.Length];
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            valueParameters[i] = args[i];
                        }
                        return (T)constructorInfo.Invoke(valueParameters);
                    }
                }
                throw new Exception("No matching constructor found.");
            });
            staticCache.Add("new", 2);
        }
        else
        {
            if(!type.IsAbstract || !type.IsSealed)
            {
                ((IDictionary<string, object>) staticContainer)["new"] = new Func<object[], T>(args =>
                {
                    return (T) Activator.CreateInstance(type)!;
                });
                staticCache.Add("new", 2);
            }
        }
        staticClassCache.Add((type, staticContainer), staticCache);
        PushObjectToLua<object>(staticContainer, type.Name);
    }

    private void PushObjectToLua<T>(T obj, int tag = 0) => PushObjectToLua(obj, typeof(T), tag);

    public void PushObjectToLua<T>(T obj, string name, int tag = 0)
    {
        if (obj == null)
        {
            Luau.lua_pushnil(state);
            return;
        }
        switch (obj)
        {
            case string str:
                Luau.lua_pushstring(state, str);
                break;
            case bool b:
                Luau.lua_pushboolean(state, b ? 1 : 0);
                break;
            case int i:
                Luau.lua_pushinteger(state, i);
                break;
            case double d:
                Luau.lua_pushnumber(state, d);
                break;
            default:
                GCHandle handle = GCHandle.Alloc(obj);
                typeHandles.Add(handle);
                Luau.lua_pushlightuserdatatagged(state, GCHandle.ToIntPtr(handle), 0);
                Luau.lua_createtable(state, 0, 0);
                Luau.lua_pushcclosurek(state, _ => LuaIndexFunction(state, typeof(T), tag), obj.GetType().FullName! + "__index", 0, (_,_) => 0);
                Luau.lua_setfield(state, -2, "__index");
                Luau.lua_pushcclosurek(state, _ => LuaNewIndexFunction(state, typeof(T), tag), obj.GetType().FullName! + "__newindex", 0, (_,_) => 0);
                Luau.lua_setfield(state, -2, "__newindex");
                Luau.lua_setmetatable(state, -2);
                break;
        }
        Luau.lua_setfield(state, Luau.LUA_GLOBALSINDEX, name);
    }
    
    public void PushObjectToLua(object? obj, Type type, int tag = 0)
    {
        if (obj == null)
        {
            Luau.lua_pushnil(state);
            return;
        }
        if (type == typeof(string))
            Luau.lua_pushstring(state, (string)obj);
        else if (type == typeof(bool))
            Luau.lua_pushboolean(state, (bool)obj ? 1 : 0);
        else if (type == typeof(int))
            Luau.lua_pushinteger(state, (int)obj);
        else if (type == typeof(double))
            Luau.lua_pushnumber(state, (double)obj);
        else
        {
            // Treat as userdata
            GCHandle handle = GCHandle.Alloc(obj);
            typeHandles.Add(handle);
            Luau.lua_pushlightuserdatatagged(state, GCHandle.ToIntPtr(handle), 0);
            Luau.lua_createtable(state, 0, 0);
            Luau.lua_pushcclosurek(state, s => LuaIndexFunction(s, type, tag), type.FullName + "__index", 0, (_, _) => 0);
            Luau.lua_setfield(state, -2, "__index");
            Luau.lua_pushcclosurek(state, s => LuaNewIndexFunction(s, type, tag), type.FullName + "__newindex", 0, (_, _) => 0);
            Luau.lua_setfield(state, -2, "__newindex");
            Luau.lua_setmetatable(state, -2);
        }
    }
    
    private int LuaIndexFunction(Luau.lua_State* luaState, Type type, int tag)
    {
        object? obj = GetObjectFromLua(luaState, 1, tag);
        string? memberName = ReadString(luaState, 2);
        if (memberName == null || obj == null)
            throw new Exception("Cannot get member name or obj is null!");
        var dictType = typeof(IDictionary<,>);
        if (obj.GetType().GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == dictType))
        {
            bool isExpando = obj is ExpandoObject;
            Type keyType;
            Type valueType;
            if (isExpando)
            {
                keyType = typeof(string);
                valueType = typeof(object);
            }
            else
            {
                keyType = obj.GetType().GetGenericArguments()[0];
                valueType = obj.GetType().GetGenericArguments()[1];
            }
            object? key = Convert.ChangeType(memberName, keyType);
            bool containsKey;
            if (isExpando)
            {
                containsKey = ((ExpandoObject) obj).Count(x => x.Key == (string) key) > 0;
            }
            else
            {
                var containsKeyMethod = obj.GetType().GetMethod("ContainsKey");
                containsKey = (bool) containsKeyMethod!.Invoke(obj, new object[] {key});
            }
            if (containsKey)
            {
                object? value = null;
                if (isExpando)
                {
                    IDictionary<string, object>? m = obj as IDictionary<string, object>;
                    if (m!.ContainsKey((string) key))
                        value = m[(string) key];
                }
                else
                {
                    var itemProperty = obj.GetType().GetProperty("Item");
                    value = itemProperty.GetValue(obj, new object[] { key });
                }
                if (value == null) return 0;
                if(value is Delegate delegateValue)
                {
                    ExpandoObject expandoObject = (ExpandoObject) obj;
                    Dictionary<string, int> staticCache = GetStaticCacheFromExpandoObject(expandoObject);
                    int scv = staticCache[(string) key];
                    if(scv == 2)
                        Luau.lua_pushcclosurek(luaState, s => InvokeDelegateFromLua(s, delegateValue, expandoObject), memberName, 0, (_, _) => 0);
                    else
                    {
                        int p = Luau.lua_gettop(luaState);
                        if (p > 0)
                            InvokeDelegateFromLua(luaState, delegateValue, expandoObject);
                        else
                            delegateValue.DynamicInvoke(true, null);
                    }
                }
                else
                    PushValueToLua(luaState, value, tag);
                return 1;
            }
            Luau.lua_pushnil(luaState);
            return 1;
        }
        var field = type.GetField(memberName);
        if (field != null)
        {
            object? fieldValue = field.GetValue(obj);
            PushValueToLua(luaState, fieldValue, tag);
            return 1;
        }
        var property = type.GetProperty(memberName);
        if (property != null && property.CanRead)
        {
            object? propertyValue = property.GetValue(obj);
            PushValueToLua(luaState, propertyValue, tag);
            return 1;
        }

        // Check if it's a method
        // TODO: It's a method, just get the parameter count and type to get the right method
        //var method = type.GetMethod(memberName);
        //if (method != null)
        {
            // Push the method as a Lua callable function
            Luau.lua_pushcclosurek(luaState, s =>
            {
                List<(Type?, object?)> newobjs = new List<(Type?, object?)>();
                (object?, List<(Type?, object?)>) p = GetLuaMethodFunctionParameters(s, tag);
                if (p.Item1 == null) return 0;
                MethodInfo[] methods = type.GetMethods();
                MethodInfo method = methods.First(x =>
                {
                    if (x.Name != memberName) return false;
                    ParameterInfo[] parameters = x.GetParameters();
                    if (parameters.Length != p.Item2.Count) return false;
                    bool canCast = true;
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        newobjs.Clear();
                        object? o = p.Item2.ElementAt(i).Item2;
                        if (parameters[i].ParameterType == p.Item2.ElementAt(i).Item1)
                        {
                            newobjs.Add((parameters[i].ParameterType, o));
                            continue;
                        }
                        try
                        {
                            if (!parameters[i].ParameterType.IsPrimitive)
                            {
                                newobjs.Add((parameters[i].ParameterType, o));
                                continue;
                            }
                            if (o == null)
                            {
                                newobjs.Add((parameters[i].ParameterType, o));
                                continue;
                            }
                            var test = Convert.ChangeType(o, parameters[i].ParameterType);
                            newobjs.Add((parameters[i].ParameterType, test));
                            break;
                        }
                        catch (Exception)
                        {
                            canCast = false;
                        }
                    }
                    return canCast;
                });
                p.Item2 = newobjs;
                return LuaMethodFunction(s, method, p.Item1, p.Item2.Select(x => x.Item2).ToArray(), tag);
            }, memberName, 0, (_, _) => 0);
            return 1;
        }
    }

    private int LuaNewIndexFunction(Luau.lua_State* luaState, Type type, int tag)
    {
        object? obj = GetObjectFromLua(luaState, 1, tag);
        string? memberName = ReadString(luaState, 2);
        object? newValue = GetLuaValue(luaState, 3, tag);

        if (memberName == null || newValue == null || obj == null)
            throw new Exception("Cannot index new function because the obj, member, or value is null!");
        var dictType = typeof(IDictionary<,>);
        if (obj.GetType().GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == dictType))
        {
            bool isExpando = obj is ExpandoObject;
            Type keyType;
            Type valueType;
            if (isExpando)
            {
                keyType = typeof(string);
                valueType = typeof(object);
            }
            else
            {
                keyType = obj.GetType().GetGenericArguments()[0];
                valueType = obj.GetType().GetGenericArguments()[1];
            }
            object? key = Convert.ChangeType(memberName, keyType);
            object? value = Convert.ChangeType(newValue, valueType);
            if (isExpando)
            {
                IDictionary<string, object>? m = obj as IDictionary<string, object>;
                if (m!.ContainsKey((string) key))
                    ((Delegate) m[(string) key]).DynamicInvoke(false, value);
                else
                    m.Add((string)key, value);
            }
            else
            {
                var itemProperty = obj.GetType().GetProperty("Item");
                itemProperty.SetValue(obj, value, new object[] {key});
            }
            return 0;
        }
        var field = type.GetField(memberName);
        if (field != null && field.FieldType.IsAssignableFrom(newValue.GetType()))
        {
            field.SetValue(obj, newValue);
            return 0;
        }
        var property = type.GetProperty(memberName);
        if (property != null && property.CanWrite && property.PropertyType.IsAssignableFrom(newValue.GetType()))
        {
            property.SetValue(obj, newValue);
            return 0;
        }
        return 0;
    }

    private (object?, List<(Type?, object?)>) GetLuaMethodFunctionParameters(Luau.lua_State* luaState, int tag)
    {
        object? obj = GetObjectFromLua(luaState, 1, tag);
        int numberOfParameters = Luau.lua_gettop(luaState);
        object?[] args = new object?[numberOfParameters];
        for (int i = 0; i < numberOfParameters; i++)
        {
            int index = i + 2;
            object? value = GetLuaValue(luaState, index, tag);
            if (value == null)
            {
                args[i] = null;
                continue;
            }
            args[i] = value;
        }
        List<(Type?, object?)> objs = new List<(Type?, object?)>();
        foreach (object? o in args)
            objs.Add((o?.GetType(), o));
        if(objs.Count > 0 && objs.ElementAt(objs.Count - 1).Item1 == null && objs.ElementAt(objs.Count - 1).Item2 == null)
            objs.RemoveAt(objs.Count - 1);
        return (obj, objs);
    }
    
    private int LuaMethodFunction(Luau.lua_State* luaState, MethodInfo method, object obj, object?[] args, int tag)
    {
        object? result = method.Invoke(obj, args);
        if (result != null)
        {
            PushValueToLua(luaState, result, tag);
            return 1;
        }
        return 0;
    }
    
    private int InvokeDelegateFromLua(Luau.lua_State* luaState, Delegate del, ExpandoObject o)
    {
        int luaParameters = Luau.lua_gettop(luaState);
        List<object?> args = new List<object?>();
        for (int i = 0; i < luaParameters; i++)
            args.Add(GetLuaValue(luaState, i + 1));
        // TODO: Correct indexing to avoid having to do this
        args.RemoveAll(x => x == o);
        object? result = del.DynamicInvoke(true, args.ToArray());
        if (del.Method.ReturnType != typeof(void))
        {
            PushValueToLua(luaState, result);
            return 1;
        }
        return 0;
    }
    
    private static T? GetObjectFromLua<T>(Luau.lua_State* luaState, int index, int tag) => (T?) GetObjectFromLua(luaState, index, tag);
    
    private static object? GetObjectFromLua(Luau.lua_State* luaState, int index, int tag)
    {
        IntPtr ptr = Luau.lua_tolightuserdatatagged(luaState, index, tag);
        GCHandle handle = GCHandle.FromIntPtr(ptr);
        return handle.Target;
    }

    internal static object? GetLuaValue(Luau.lua_State* luaState, int index, int tag = 0)
    {
        if (Luau.lua_isnil(luaState, index))
            return null;
        if (Luau.lua_isnumber(luaState, index) == 1)
            return ReadNumber(luaState, index);
        if (Luau.lua_isstring(luaState, index) == 1)
            return ReadString(luaState, index);
        if (Luau.lua_isboolean(luaState, index))
            return Luau.lua_toboolean(luaState, index);
        if (Luau.lua_isfunction(luaState, index))
            return ReadFunction(luaState, index);
        if (Luau.lua_isuserdata(luaState, index) == 1)
        {
            IntPtr userdataPtr = Luau.lua_tolightuserdatatagged(luaState, index, tag);
            GCHandle handle = GCHandle.FromIntPtr(userdataPtr);
            return handle.Target;
        }
        return null;
    }

    private static void CacheHandle(ref List<GCHandle> types, ref GCHandle handle) => types.Add(handle);

    internal static void PushValueToLua(Luau.lua_State* luaState, object? value, ref List<GCHandle> typeHandles, int tag = 0)
    {
        if (value is string)
        {
            Luau.lua_pushstring(luaState, (string)value);
            return;
        }
        if (value is int)
        {
            Luau.lua_pushinteger(luaState, (int)value);
            return;
        }
        if (value is double || value is float)
        {
            Luau.lua_pushnumber(luaState, Convert.ToDouble(value));
            return;
        }
        if (value is bool)
        {
            Luau.lua_pushboolean(luaState, (bool)value ? 1 : 0);
            return;
        }
        if (value == null) return;
        if(!IsTypeAllowed(value.GetType()))
        {
            Luau.lua_pushnil(luaState);
            return;
        }
        GCHandle handle = GCHandle.Alloc(value);
        CacheHandle(ref typeHandles, ref handle);
        Luau.lua_pushlightuserdatatagged(luaState, GCHandle.ToIntPtr(handle), tag);
    }

    private void PushValueToLua(Luau.lua_State* luaState, object? value, int tag = 0) =>
        PushValueToLua(luaState, value, ref typeHandles, tag);

    public void Dispose() => typeHandles.ForEach(x => x.Free());
}