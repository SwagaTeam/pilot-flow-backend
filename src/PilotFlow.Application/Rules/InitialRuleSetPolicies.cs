namespace PilotFlow.Application.Rules;

public static class InitialRuleSetPolicies
{
    public const string Version = "fck-2026-tz-v1";

    public static IReadOnlyList<RulePolicy> Policies { get; } = Array.AsReadOnly<RulePolicy>(
    [
        new("REVENUE_MINIMUM", "Доля выручки от 10% включительно проходит проверку допуска.", "proposed"),
        new("DURATION_MAXIMUM", "Календарная длительность каждого продукта потока не превышает 30 дней.", "source"),
        new("DENSE_RANK", "Равные значения получают равное место. Следующее отличное значение получает следующее место.", "proposed"),
        new("COMPARISON_POOL", "В относительных критериях сравниваются допущенные потоки.", "proposed"),
        new("MAXIMUM_MARGIN_SCORE", "Максимальная неотрицательная маржинальность получает 3 балла.", "source"),
        new("ZERO_MARGIN", "Нулевая маржинальность получает 1,5 балла, если она ниже максимальной.", "proposed"),
        new("GROWTH_AT_TEN", "Рост продаж ровно 10% получает 1 балл.", "proposed"),
        new("LOAD_AT_FIFTY", "Загрузка ровно 50% получает 2 балла.", "source"),
        new("NEGATIVE_GROWTH", "Отрицательный рост не оценивается в баллах и блокирует окончательный выбор по рейтингу.", "unresolved"),
        new("NEGATIVE_MAXIMUM_MARGIN", "Если максимальная маржинальность отрицательна, критерий остаётся без баллов у всех кандидатов, а окончательный выбор по рейтингу блокируется.", "unresolved"),
        new("INCOMPLETE_RANKING", "При неполных данных рейтинг предварительный. Итоговый балл и место не определены.", "proposed"),
        new("CYCLE_DIFFERENCE", "Разницу максимального и минимального времени цикла делят на минимум и умножают на 100. При нулевом минимуме или неизвестном времени цикла результат неизвестен. Разница свыше 20% требует обоснования состава.", "proposed"),
    ]);
}
