﻿namespace RealArtists.ShipHub.Api.Controllers {
  using System.Threading.Tasks;
  using System.Web.Http;
  using Common.DataModel;

  [RoutePrefix("test/entity")]
  public class EntityTestController : ShipHubController {
    const long UserId = -42;

    [HttpGet]
    [Route("user")]
    public async Task<IHttpActionResult> TestUser() {
      var account = Context.Accounts.Add(new User() {
        Id = UserId,
        Login = "test",
      });
      await Context.SaveChangesAsync();

      Context.Accounts.Remove(account);
      await Context.SaveChangesAsync();

      return Ok("Ok");
    }

    [HttpGet]
    [Route("accessToken")]
    public async Task<IHttpActionResult> AccessToken() {
      var account = (User)Context.Accounts.Add(new User() {
        Id = UserId,
        Login = "test",
      });
      var token = Context.AccessTokens.Add(new AccessToken() {
        Account = account,
        ApplicationId = "efTest",
        Scopes = "testing,more.testing",
        Token = "thingsAndStuffAndThings"
      });
      await Context.SaveChangesAsync();

      Context.AccessTokens.Remove(token);
      Context.Accounts.Remove(account);
      await Context.SaveChangesAsync();

      return Ok("Ok");
    }
  }
}