using Microsoft.Playwright;
using Unpwn.Application.Recovery;

namespace Unpwn.Automation.Recovery;

internal sealed class SyntheticPasswordChangePage(IPage page)
{
    private const string ExpectedPage = "body[data-unpwn-provider='synthetic'][data-unpwn-workflow='password-change']";
    private readonly IPage _page = page ?? throw new ArgumentNullException(nameof(page));

    public async Task<BrowserAssistanceState> InspectAsync()
    {
        if (await _page.Locator("[data-unpwn-stop-reason='mfa']").CountAsync() > 0)
        {
            return BrowserAssistanceState.PausedForMfa;
        }

        if (await _page.Locator("[data-unpwn-stop-reason='captcha']").CountAsync() > 0)
        {
            return BrowserAssistanceState.PausedForCaptcha;
        }

        if (await _page.Locator("[data-unpwn-stop-reason='email-link']").CountAsync() > 0)
        {
            return BrowserAssistanceState.PausedForEmailLink;
        }

        if (await _page.Locator(ExpectedPage).CountAsync() != 1 ||
            await _page.GetByTestId("new-password").CountAsync() != 1 ||
            await _page.GetByTestId("confirm-password").CountAsync() != 1 ||
            await _page.GetByTestId("submit-password-change").CountAsync() != 1)
        {
            return BrowserAssistanceState.ManualGuidanceRequired;
        }

        return BrowserAssistanceState.ReadyForAuthorization;
    }

    public async Task<bool> SubmitAsync(string newPassword)
    {
        await _page.GetByTestId("new-password").FillAsync(newPassword);
        await _page.GetByTestId("confirm-password").FillAsync(newPassword);
        await _page.GetByTestId("submit-password-change").ClickAsync();
        return await _page.Locator("[data-unpwn-outcome='submitted']").CountAsync() == 1;
    }
}
