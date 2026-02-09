namespace DTR.Core;

public class AndRuleSpecification : IRuleSpecification
{
    private readonly IRuleSpecification _left;
    private readonly IRuleSpecification _right;

    public AndRuleSpecification(IRuleSpecification left, IRuleSpecification right)
    {
        _left = left;
        _right = right;
    }

    public bool IsSatisfiedBy(TimeRange input, TimeContext context) =>
        _left.IsSatisfiedBy(input, context) && _right.IsSatisfiedBy(input, context);
}


//public class AndSpec : IRuleSpecification
//{
//    private readonly IRuleSpecification[] _specs;

//    public AndSpec(params IRuleSpecification[] specs)
//    {
//        _specs = specs;
//    }

//    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
//    {
//        var payload = context.Payload;

//        foreach (var spec in _specs)
//        {
//            var key = SpecEvaluationCache.CreateKey(
//                spec.GetType().Name,
//                payload.Data.CurrentDate,
//                payload.Data.Employee.Id
//            );

//            if (payload.SharedSpecCache.GetByKey(key, out var cached))
//            {
//                if (!cached) return false;
//                continue;
//            }
//            var result = spec.IsSatisfiedBy(input, context);
//            payload.SharedSpecCache.Record(key, result);
//            if (!result) return false;
//        }
//        return true;
//    }
//}










