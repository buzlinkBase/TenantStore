namespace DTR.Core;

//[
//  {
//    "name": "Weekend OT Rule",
//    "type": "And",
//    "rules": [
//      { "type": "IsWeekend" },
//      { "type": "IsOvertime" }
//    ],
//    "tag": "Weekend OT"
//  },
//  {
//    "name": "Late Rule",
//    "type": "Single",
//    "rule": { "type": "IsLate" },
//    "tag": "Late"
//  }
//]
//public class RuleDefinition
//{
//    public string Name { get; set; }
//    public string Type { get; set; } // "Single", "And", "Or"
//    public string Tag { get; set; }
//    public List<RuleDefinition> Rules { get; set; }
//    public RuleDefinition Rule { get; set; }
//}


//public class RuleFactory
//{
//    public static IRuleSpecification Build(RuleDefinition def)
//    {
//        return def.Type switch
//        {
//            "Single" => CreateSpec(def.Rule.Type),
//            "And" => def.Rules.Select(r => Build(r)).Aggregate((a, b) => a.And(b)),
//            "Or" => def.Rules.Select(r => Build(r)).Aggregate((a, b) => a.Or(b)),
//            _ => throw new NotSupportedException($"Unknown rule type: {def.Type}")
//        };
//    }

//    private static IRuleSpecification CreateSpec(string type) =>
//        type switch
//        {
//            "IsWeekend" => new IsWeekendRule(),
//            "IsOvertime" => new IsOvertimeRule(),
//            "IsLate" => new IsLateRule(),
//            _ => throw new NotSupportedException($"Unknown spec: {type}")
//        };
//}


// Step 4: Load and Apply Rule
//var json = File.ReadAllText("rules.json");
//var definitions = JsonSerializer.Deserialize<List<RuleDefinition>>(json);

//foreach (var def in definitions)
//{
//    var spec = RuleFactory.Build(def);
//    var rule = new ConditionalRule(spec, r => r.Tag = def.Tag);

//    foreach (var range in timeRangeCollection)
//    {
//        if (rule.IsSatisfiedBy(range, context))
//            rule.Apply(range);
//    }
//}
