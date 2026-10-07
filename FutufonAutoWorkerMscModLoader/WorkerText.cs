namespace FutufonAutoWorkerMscModLoader
{
    internal static class WorkerText
    {
        internal static string Pick(bool russian, string ru, string en) { return russian ? ru : en; }
        internal static string Mode(bool ru, AutomationMode mode)
        {
            switch (mode)
            {
                case AutomationMode.ManualDelivery: return Pick(ru, "Переноска вручную", "Manual delivery");
                case AutomationMode.LoosePackages: return Pick(ru, "Готовые коробки стопками", "Loose package stacks");
                case AutomationMode.ManualSupplies: return Pick(ru, "Компоненты вручную", "Manual supplies");
                default: return Pick(ru, "Полный цикл", "Full cycle");
            }
        }
        internal static string Supply(bool ru, int index)
        {
            switch (index)
            {
                case 0: return Pick(ru, "зарядки", "chargers");
                case 1: return Pick(ru, "инструкции", "manuals");
                case 2: return Pick(ru, "лотки", "trays");
                case 3: return Pick(ru, "упаковка", "sheets");
                default: return Pick(ru, "вернись к столу", "return to the table");
            }
        }
        internal static string SelectedMode(bool ru, AutomationMode selected, AutomationMode? running)
        {
            return (running.HasValue && running.Value != selected ? Pick(ru, "Смена на: ", "Changing to: ") : "") + Mode(ru, selected);
        }
        internal static string Volume(bool ru, BatchVolume volume)
        {
            return volume == BatchVolume.OneCarton ? Pick(ru, "Одна коробка (44)", "One carton (44)") :
                Pick(ru, "Палета (до 176)", "Pallet (up to 176)");
        }
        internal static string IdleCounter(bool ru, float minutes)
        {
            return Pick(ru, "Простой: ", "Total idle: ") + minutes.ToString("F1") + Pick(ru, " мин", " min");
        }
        internal static string Phase(bool ru, WorkerPhase phase, int detail, AutomationMode mode)
        {
            switch (phase)
            {
                case WorkerPhase.Preparing: return Pick(ru, "Подготовка рабочего места", "Preparing workstation");
                case WorkerPhase.Collecting: return Pick(ru, "Беру компоненты", "Collecting parts");
                case WorkerPhase.Assembling: return Pick(ru, "Собираю комплект", "Assembling tray");
                case WorkerPhase.Folding: return Pick(ru, "Складываю упаковку", "Folding package");
                case WorkerPhase.Closing: return Pick(ru, "Закрываю коробку", "Closing package");
                case WorkerPhase.Packing: return Pick(ru, "Кладу в большую коробку", "Packing shipping carton");
                case WorkerPhase.Delivering: return Pick(ru, "Ставлю на палету", "Delivering to pallet");
                case WorkerPhase.WaitingStock: return Pick(ru, "Принеси на стол: ", "Bring to the table: ") + Supply(ru, detail);
                case WorkerPhase.WaitingOpenStock: return Pick(ru, "Открой коробку: ", "Open supply box: ") + Supply(ru, detail);
                case WorkerPhase.WaitingDelivery: return Pick(ru, "Отнеси полную коробку на палету", "Carry the full carton to a pallet");
                case WorkerPhase.WaitingStacks: return Pick(ru, "Освободи место в стопках", "Clear space in the output stacks");
                case WorkerPhase.Returning: return Pick(ru, "Вернись к рабочему столу", "Return to the workstation");
                case WorkerPhase.Paused: return Pick(ru, "Пауза. F8 — продолжить", "Paused. F8 resumes");
                case WorkerPhase.Error: return Pick(ru, "Сборка остановлена. Подробности в логе", "Assembly stopped. See the log for details");
                case WorkerPhase.Done:
                    if (mode == AutomationMode.FullCycle) return Pick(ru, "Готово: коробки на палете", "Done: cartons delivered to pallet");
                    if (mode == AutomationMode.ManualDelivery) return Pick(ru, "Готово: отнеси коробку на палету", "Done: carry the carton to a pallet");
                    return Pick(ru, "Готово: коробки в стопках", "Done: packages are in stacks");
                default: return Pick(ru, "Посмотри на свободный стол и нажми F8", "Look at a clear table and press F8");
            }
        }
        internal static string Supervisor(bool ru, WorkCheckState state)
        {
            if (!ru) return WorkCheckStatus.Text(state);
            switch (state)
            {
                case WorkCheckState.Working: return "Начальник: игра засчитывает работу";
                case WorkCheckState.Slacking: return "Начальник: обнаружен простой!";
                case WorkCheckState.NotClockedIn: return "Начальник: приход не отмечен";
                case WorkCheckState.Waiting: return "Начальник: ждём проверку";
                case WorkCheckState.Paused: return "Начальник: проверка приостановлена";
                case WorkCheckState.Checking: return "Начальник: идёт проверка";
                default: return "Начальник: нет активной проверки";
            }
        }
        internal static string Notice(bool ru, ShiftNotice notice)
        {
            return notice == ShiftNotice.Lunch ?
                Pick(ru, "11:00 — время обеда", "11:00 — lunch break") :
                Pick(ru, "16:00 — конец смены. Не забудь отметить уход", "16:00 — shift finished. Remember to clock out");
        }
        internal static string Error(bool ru, string detail)
        {
            if (detail == null) return Phase(ru, WorkerPhase.Error, 0, AutomationMode.FullCycle);
            if (detail.Contains("Look down") || detail.Contains("Not enough table space"))
                return Pick(ru, "Посмотри вниз на свободный участок стола", "Look down at a clear part of the table");
            if (detail.Contains("Clear finished"))
                return Pick(ru, "Убери детали с участка сборки", "Clear loose parts from the assembly area");
            if (detail.Contains("No free player pallet"))
                return Pick(ru, "На палете нет места — освободи её", "No free pallet slots — clear a pallet");
            if (detail.Contains("four package stacks"))
                return Pick(ru, "Освободи стол рядом для четырёх стопок", "Clear nearby table space for four stacks");
            if (detail.Contains("No floor space"))
                return Pick(ru, "Рядом нет места для большой коробки", "No floor space nearby for the shipping carton");
            if (detail.Contains("existing full carton"))
                return Pick(ru, "Сначала отнеси готовую большую коробку на палету", "Deliver the existing full carton to a pallet first");
            if (detail.Contains("not found nearby"))
                return Pick(ru, "Компоненты завода не найдены рядом", "Factory supplies not found nearby");
            return Phase(ru, WorkerPhase.Error, 0, AutomationMode.FullCycle);
        }
    }
}
