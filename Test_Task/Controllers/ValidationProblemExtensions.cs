using Microsoft.AspNetCore.Mvc;

namespace Test_Task.Controllers;

public static class ValidationProblemExtensions
{
    public static IActionResult ToValidationProblem(
        this ControllerBase controller,
        IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (key, messages) in errors)
        {
            foreach (var message in messages)
            {
                controller.ModelState.AddModelError(key, message);
            }
        }

        return controller.ValidationProblem(controller.ModelState);
    }
}
