namespace DTR.Core;

public class ClientPolicyProvider 
{
    private readonly Dictionary<ClientPolicyKey, ClientPolicyRule>? _policies;
    public ClientPolicyProvider(Dictionary<ClientPolicyKey, ClientPolicyRule>? policies )
    {
        _policies = policies;
    }

     public ClientPolicyRule? GetPolicy(Guid? clientId)
    {
        if (_policies == null || clientId==null || clientId==Guid.Empty) return default; 
        var key=new ClientPolicyKey(clientId.Value);
        if (_policies.TryGetValue(key,out var policy))
        {
            return policy;  
        }
        return default;
    }
}
