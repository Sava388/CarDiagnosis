using System;

namespace CarDiagnosis.Core
{
    public enum WheelPlace { Car, Hands, Changer, Balancer }
    public enum TireAction { Remove, PlaceOnChanger, TakeFromChanger, Dismount, Mount, PlaceOnBalancer, TakeFromBalancer, Spin, ApplyWeight, Install, Control }

    // Состояние одного обслуживаемого колеса. Колесо всегда ровно в одном месте (Place) — дублирование невозможно.
    public sealed class TireJob
    {
        public WheelPlace Place = WheelPlace.Car;
        public bool EverRemoved, OldTireOff, NewTireOn, Measured, Balanced, Controlled;
        public bool Installed = true;
        public int WeightApplied;       // 0 = груз не установлен
        public int LastReading = -1;    // -1 = измерений не было
        public bool Serviced => NewTireOn && Balanced;
        public bool Carried => Place == WheelPlace.Hands;
    }

    public static class TireRules
    {
        public static int Cost(TireServiceDef d, TireAction a)
        {
            switch (a)
            {
                case TireAction.Remove: return d.removeCost;
                case TireAction.Dismount: return d.dismountCost;
                case TireAction.Mount: return d.mountCost;
                case TireAction.Spin: return d.spinCost;
                case TireAction.ApplyWeight: return d.weightCost;
                case TireAction.Install: return d.installCost;
                case TireAction.Control: return d.controlCost;
                default: return 0;   // перенос колеса между точками время не тратит
            }
        }

        public static string TireLabel(TireJob j, TireServiceDef d)
            => j == null ? d.fromTire : j.NewTireOn ? d.toTire : j.OldTireOff ? "шина снята" : d.fromTire;

        // null = действие допустимо, иначе понятная причина отказа.
        public static string Refusal(TireJob j, TireServiceDef d, TireAction a, int grams)
        {
            switch (a)
            {
                case TireAction.Remove:
                    return j.Place != WheelPlace.Car ? "Колесо уже снято." : j.Controlled && j.Serviced ? "Колесо проверено, заказ можно завершать." : null;
                case TireAction.PlaceOnChanger:
                case TireAction.PlaceOnBalancer:
                    if (j.Place != WheelPlace.Hands) return "Колеса нет в руках.";
                    if (a == TireAction.PlaceOnBalancer && !j.NewTireOn) return "Сначала смените шину на шиномонтажном станке.";
                    if (a == TireAction.PlaceOnChanger && j.Serviced) return "Шина уже заменена и колесо отбалансировано.";
                    return null;
                case TireAction.TakeFromChanger: return j.Place == WheelPlace.Changer ? null : "На станке нет колеса.";
                case TireAction.TakeFromBalancer: return j.Place == WheelPlace.Balancer ? null : "На станке нет колеса.";
                case TireAction.Dismount:
                    if (j.Place != WheelPlace.Changer) return "Установите колесо на шиномонтажный станок.";
                    return j.OldTireOff || j.NewTireOn ? "Старая шина уже снята." : null;
                case TireAction.Mount:
                    if (j.Place != WheelPlace.Changer) return "Установите колесо на шиномонтажный станок.";
                    if (j.NewTireOn) return "Новая шина уже установлена.";
                    return j.OldTireOff ? null : "Сначала демонтируйте старую шину.";
                case TireAction.Spin:
                    if (j.Place != WheelPlace.Balancer) return "Установите колесо на балансировочный станок.";
                    return j.Balanced ? "Колесо уже отбалансировано." : null;
                case TireAction.ApplyWeight:
                    if (j.Place != WheelPlace.Balancer) return "Установите колесо на балансировочный станок.";
                    if (j.Balanced) return "Колесо уже отбалансировано.";
                    if (!j.Measured) return "Сначала запустите станок и измерьте дисбаланс.";
                    if (j.WeightApplied > 0) return "Груз установлен. Запустите контрольный прогон.";
                    return d.weightOptions != null && Array.IndexOf(d.weightOptions, grams) >= 0 ? null : "Такого груза нет.";
                case TireAction.Install:
                    return j.Place == WheelPlace.Hands ? null : "Колеса нет в руках.";
                case TireAction.Control:
                    if (j.Place != WheelPlace.Car || !j.Installed) return "Колесо не установлено на автомобиль.";
                    if (!j.EverRemoved) return "Колесо ещё не обслуживалось.";
                    return j.Controlled ? "Контроль уже выполнен." : null;
            }
            return "Неизвестное действие.";
        }

        // Применяет допустимое действие и возвращает текст результата.
        public static string Apply(TireJob j, TireServiceDef d, TireAction a, int grams)
        {
            switch (a)
            {
                case TireAction.Remove:
                    j.Place = WheelPlace.Hands; j.Installed = false; j.Controlled = false; j.EverRemoved = true;
                    return "Колесо снято и у вас в руках.";
                case TireAction.PlaceOnChanger: j.Place = WheelPlace.Changer; return "Колесо закреплено на шиномонтажном станке.";
                case TireAction.TakeFromChanger: j.Place = WheelPlace.Hands; return "Колесо снято со станка.";
                case TireAction.PlaceOnBalancer: j.Place = WheelPlace.Balancer; return "Колесо установлено на вал балансировочного станка.";
                case TireAction.TakeFromBalancer: j.Place = WheelPlace.Hands; return "Колесо снято с балансировочного станка.";
                case TireAction.Dismount: j.OldTireOff = true; return "Старая шина (" + d.fromTire + ") демонтирована с диска.";
                case TireAction.Mount:
                    j.NewTireOn = true; j.Measured = false; j.Balanced = false; j.WeightApplied = 0; j.LastReading = -1;
                    return "Новая шина (" + d.toTire + ") смонтирована. Нужна балансировка.";
                case TireAction.Spin:
                    if (j.WeightApplied == 0)
                    {
                        j.Measured = true; j.LastReading = d.imbalanceGrams;
                        return "Дисбаланс " + d.imbalanceGrams + " г, " + d.imbalanceSide + ".";
                    }
                    if (j.WeightApplied == d.imbalanceGrams)
                    {
                        j.Balanced = true; j.LastReading = 0;
                        return "Контрольный прогон: 0 г. Колесо отбалансировано.";
                    }
                    j.LastReading = Math.Abs(d.imbalanceGrams - j.WeightApplied); j.WeightApplied = 0;
                    return "Остаточный дисбаланс " + j.LastReading + " г. Груз не подошёл и снят — подберите другой.";
                case TireAction.ApplyWeight:
                    j.WeightApplied = grams;
                    return "Установлен груз " + grams + " г. Запустите контрольный прогон.";
                case TireAction.Install:
                    j.Place = WheelPlace.Car; j.Installed = true; j.Controlled = false;
                    return "Колесо установлено на автомобиль. Проверьте установку.";
                case TireAction.Control:
                    j.Controlled = true;
                    return "Колесо закреплено. Шина: " + TireLabel(j, d) + ". Балансировка: " + (j.Balanced ? "выполнена." : "не выполнена.");
            }
            return "";
        }

        // Минимум минут, чтобы из текущего состояния закончить заказ верно.
        public static int RemainingMinimal(TireJob j, TireServiceDef d)
        {
            int n = 0;
            if (!j.Serviced)
            {
                if (j.Place == WheelPlace.Car) n += d.removeCost;
                if (!j.NewTireOn) { if (!j.OldTireOff) n += d.dismountCost; n += d.mountCost; }
                if (!j.Balanced)
                {
                    if (j.NewTireOn && j.WeightApplied == d.imbalanceGrams) n += d.spinCost;
                    else { if (!(j.NewTireOn && j.Measured)) n += d.spinCost; n += d.weightCost + d.spinCost; }
                }
                n += d.installCost + d.controlCost;
            }
            else
            {
                if (j.Place != WheelPlace.Car) n += d.installCost;
                if (!j.Controlled) n += d.controlCost;
            }
            return n;
        }

        public static int MinimalTotal(TireServiceDef d) => RemainingMinimal(new TireJob(), d);

        public static string NextHint(TireJob j, TireServiceDef d)
        {
            switch (j.Place)
            {
                case WheelPlace.Car:
                    if (j.Controlled) return j.Serviced ? "Завершите заказ у колеса (E)" : "Работа не закончена: снимите колесо";
                    if (j.Serviced) return "Проверьте установку колеса (E)";
                    return "Снимите колесо: " + d.wheelName.ToLowerInvariant();
                case WheelPlace.Hands:
                    if (j.Serviced) return "Установите колесо на автомобиль";
                    return j.NewTireOn ? "Отнесите колесо к балансировочному станку" : "Отнесите колесо к шиномонтажному станку";
                case WheelPlace.Changer:
                    if (!j.OldTireOff && !j.NewTireOn) return "Демонтируйте старую шину (" + d.fromTire + ")";
                    if (!j.NewTireOn) return "Смонтируйте новую шину (" + d.toTire + ")";
                    return "Снимите колесо и отнесите к балансировочному станку";
                case WheelPlace.Balancer:
                    if (j.Balanced) return "Снимите колесо со станка и верните на автомобиль";
                    if (j.WeightApplied > 0) return "Запустите контрольный прогон";
                    return j.Measured ? "Установите груз по показанию станка" : "Запустите станок и измерьте дисбаланс";
            }
            return "";
        }
    }
}
