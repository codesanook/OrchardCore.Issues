
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using YesSql;

public class HomeController : Controller
{
    private readonly ISession _session;

    public HomeController(ISession session)
    {
        this._session = session;
    }

    [Route("/home")]
    public async Task<ActionResult> Index()
    {
        await _session.FlushAsync();
        return Content("ok");
    }

}
