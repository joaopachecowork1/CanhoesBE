using Microsoft.AspNetCore.Mvc;
using Canhoes.Api.DTOs;
using Canhoes.Api.Auth;

namespace Canhoes.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    // Auth logic is handled via Google OIDC and UserContextMiddleware.
    // Local login is disabled for security reasons.
}
