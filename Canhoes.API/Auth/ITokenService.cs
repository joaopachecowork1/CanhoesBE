namespace Canhoes.Api.Auth
{
    public interface ITokenService
    {
        string GenerateToken(string email);
    }
}
