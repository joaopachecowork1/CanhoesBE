using Microsoft.AspNetCore.Mvc;
using Canhoes.Api.DTOs;
using Canhoes.Api.Auth;

namespace Canhoes.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ITokenService _tokenService;

    // Inje��o de depend�ncia mais limpa
    public AuthController(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    [HttpPost("login")]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest req)
    {
        var email = req.Email.Trim();
        var token = _tokenService.GenerateToken(email);
        var displayName = email[..Math.Max(0, email.IndexOf('@'))];

        return Ok(new LoginResponse(token, displayName));
    }
}
