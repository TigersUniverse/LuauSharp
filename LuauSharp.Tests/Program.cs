using System.Numerics;
using System.Reflection;
using LuauSharp;

const string CODE =
"""
local globalTestClass = t()
print(globalTestClass:GetNameAndVersion())
print("I Like Turtles!")
printdebug(TestClass)
dict.K = 5
print(dict["K"])
local testObject1 = TestClass.new()
local testObject2 = TestClass.new()
testObject1.Name = "this is my name!"
testObject1:SetVersion(2)
print(testObject1:GetNameAndVersion())
print("Before")
testObject2.Name = "cool name"
testObject2:SetVersion(3)
print(testObject2:GetNameAndVersion())
print("After")
testObject2:Apply(testObject1)
print(testObject2:GetNameAndVersion())
print("LuauSharp Version "..version)
TestClass.Multiply = 2.5
TestClass:SetDivide(1)
print(TestClass.Divide)
print(TestClass.Multiply)
TestClass:RunIt(function()
    print("Hello World!")
end)
testObject1.Name = "new object name!"
testObject1:SetVersion(function()
    return 5
end)
print(testObject1:GetNameAndVersion())
TestClass:TestNull(nil, "yay!")
print(TestClass.TEST_NAME)
print("Is Valid 0: "..tostring(TestClass:IsValid(0)))
print("Is Valid 1: "..tostring(TestClass:IsValid(TestEnum.Valid)))
-- Uncomment Following Two Lines for exception
--printdebug(testObject1:GetInfo())
--printdebug(testObject1.Direction)
""";

TestClass globalTestClass = new TestClass
{
    Name = "GLOBAL"
};
globalTestClass.SetVersion(0);

using VM vm = new VM(Console.WriteLine, Console.WriteLine, Console.Error.WriteLine);
vm.UserData.ForwardType<TestEnum>();
vm.UserData.ForwardType<TestClass>();
vm.UserData.PushFunction("t", () => globalTestClass);
vm.UserData.PushFunction("printdebug", (Action<object>) (s => Console.WriteLine(s.GetType())));
vm.UserData.PushObjectToLua<string>("version1.0", "version");
vm.UserData.PushObjectToLua(new Dictionary<string, int>
{
    ["K"] = 1
}, "dict");
bool worked = vm.DoText("text.luau", CODE);
if(!worked) return;
vm.Execute();

class TestClass
{
    public const string TEST_NAME = "test name!";
    
    public static double Multiply = 1.5;
    public static int Divide { get; private set; } = 2;

    public static void SetDivide(int d) => Divide = d;
    
    public static void TestNull(object d, string s) => Console.WriteLine("Null parameters! " + s);
    
    public string Name;
    public int Version { get; private set; } = 1;

    public string GetNameAndVersion() => Name + Version;

    public void SetVersion(int version) => Version = version;

    public void SetVersion(LuaFunctionWrapper func)
    {
        Version = func.Call<int>();
        func.Dispose();
    }

    public void Apply(TestClass testClass)
    {
        Name = testClass.Name;
        Version = testClass.Version;
    }

    public static void RunIt(LuaFunctionWrapper func)
    {
        func.Call();
        func.Dispose();
    }

    public static bool IsValid(TestEnum testEnum) => testEnum == TestEnum.Valid;
    
    // This section has not had their types forwarded; therefore, any references to them (Vector2 and FieldInfo) will be null
    
    public Vector2 Direction = Vector2.One;

    public FieldInfo GetInfo() => typeof(TestClass).GetFields()[0];
}

public enum TestEnum
{
    Invalid = -1,
    Waiting = 0x0,
    Valid = 1
}