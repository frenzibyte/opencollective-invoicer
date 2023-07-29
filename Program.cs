// See https://aka.ms/new-console-template for more information

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic.CompilerServices;
using OpenCollectiveInvoicer.GraphQL;
using StrawberryShake;

namespace OpenCollectiveInvoicer;

public static class Program
{
    public const double HOURLY_RATE = 30;
    public const string PERSONAL_TOKEN = "<PERSONAL_TOKEN>";

    public static void Main()
    {
        // invoice items (i.e. "tasks") go in this list
        var tasks = new List<TaskMetadata>();

        var services = new ServiceCollection();

        services
            .AddOpenCollective()
            .ConfigureHttpClient(c =>
            {
                c.BaseAddress = new Uri("https://api.opencollective.com/graphql/v2");
                c.DefaultRequestHeaders.Add("Personal-Token", PERSONAL_TOKEN);
            });

        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IOpenCollective>();

        var meResult = client.GetMe.ExecuteAsync().Result;

        if (meResult.IsErrorResult())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to retrieve payee: \"{meResult.Errors.Single().Message}\"");
            Console.ForegroundColor = ConsoleColor.Gray;
            return;
        }

        var me = client.GetMe.ExecuteAsync().Result.Data!.Me!;
        var payoutMethod = me.PayoutMethods!.Single(p => p?.Type == PayoutMethodType.Paypal)!;

        Console.WriteLine($"Reporting invoice as \"{me.LegalName}\" ({me.Name}) using PayPal payout method");

        var expense = new ExpenseCreateInput
        {
            Description = "osu!dev expense",
            Type = ExpenseType.Invoice,
            Payee = new AccountReferenceInput { Id = me.Id, Slug = me.Name },
            PayoutMethod = new PayoutMethodInput
            {
                Id = payoutMethod.Id,
                Name = payoutMethod.Name,
                Type = payoutMethod.Type,
            },
            Items = tasks.Select(t => new ExpenseItemCreateInput
            {
                Description = $"[{getIntervalString(t.Interval)}] {t.Label}",
                Amount = (int)(t.Interval.TotalHours * HOURLY_RATE * 100),
                IncurredAt = t.Date,
            }).ToArray(),
        };

        Console.WriteLine("Private message to embed within the invoice: ");

        expense.PrivateMessage = Console.ReadLine();

        Console.WriteLine("Submitting expense...");

        var submission = client.CreateExpense.ExecuteAsync(expense).Result;
        
        if (submission.IsErrorResult())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to submit expense: \"{submission.Errors.Single().Message}\"");
            Console.ForegroundColor = ConsoleColor.Gray;
            return;
        }

        var result = submission.Data.CreateExpense;

        Console.WriteLine($"Expense submitted successfully, total: {result.AmountV2!.Value} {result.AmountV2.Currency?.ToString().ToUpperInvariant()}!");
        Console.WriteLine("Redirecting to invoice page...");
        Process.Start($"https://opencollective.com/ppy/expenses/{submission.Data!.CreateExpense.Id}");
    }

    private static string getIntervalString(TimeSpan interval)
    {
        const int rounding = 5;
        int roundedMinutes = (int)(Math.Ceiling((double)interval.Minutes / rounding) * rounding);
        int roundedHours = interval.Hours;

        if (roundedMinutes == 60)
        {
            roundedHours++;
            roundedMinutes = 0;
        }

        if (roundedHours > 0)
        {
            if (roundedMinutes > 0)
                return $"{roundedHours}h {roundedMinutes}m";
            else
                return $"{roundedHours}h";
        }

        return $"{roundedMinutes}m";
    }

    public record struct TaskMetadata(string Label, DateTime Date, TimeSpan Interval);
}