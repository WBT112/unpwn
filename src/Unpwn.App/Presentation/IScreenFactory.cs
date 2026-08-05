namespace Unpwn.App.Presentation;

public interface IScreenFactory
{
    ScreenViewModel Create(AppRoute route);
}
