using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Payroll_incentives
{
    public static class IncentiveDistributor
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="primaryShiftToDistributeTo"></param>
        /// <param name="incentive"></param>
        /// <param name="allExistingShifts">includes primaryShiftToDistributeTo</param>
        public static DistributionResult DistributeIncentive(Shift primaryShiftToDistributeTo, int incentive, List<Shift> allExistingShifts, DistributionRules distributionRules)
        {
            DistributionResult result = new DistributionResult();

            try
            {
                bool distributed = false;
                int maxTries = 100;//prevent infinite loop
                int tries = 0;//how many attempts we made at distributing
                int amountDistributed = 0;
                HashSet<Guid> shiftIdsTried = new HashSet<Guid>();

                //distribute to primary shift                
                decimal incentiveAmount = incentive * primaryShiftToDistributeTo.HoursWorked;
                int amountToDistribute = Shift.GetIncentiveToDistribute(primaryShiftToDistributeTo.AllIncentives, incentiveAmount, distributionRules, primaryShiftToDistributeTo.HoursWorked);
                if (amountToDistribute == 0)
                {
                    result.Errors.Add("There is nothing we can distribute based on the rules");
                }
                else
                {
                    amountDistributed = amountToDistribute;
                    primaryShiftToDistributeTo.DistributeAndLogIncentive(amountToDistribute, shiftIdsTried);
                    if (amountDistributed < incentive)
                    {
                        int amountLeftToDistribute = 0;
                        foreach (Shift shift in allExistingShifts)
                        {
                            amountLeftToDistribute = incentive - amountToDistribute;
                            bool shiftProcessed = shiftIdsTried.Contains(shift.Id);
                            if (!shiftProcessed)
                            {
                                int shiftAmountToDistribute = Shift.GetIncentiveToDistribute(shift.AllIncentives, amountLeftToDistribute, distributionRules, shift.HoursWorked);
                                if (shiftAmountToDistribute == 0)
                                {
                                    result.Errors.Add("There is nothing we can distribute based on the rules");
                                    break;
                                }
                                else
                                {
                                    amountDistributed += shiftAmountToDistribute;
                                    shift.DistributeAndLogIncentive(shiftAmountToDistribute, shiftIdsTried);
                                    if (amountDistributed == incentive)
                                    {
                                        break;//distributed everything!
                                    }
                                }
                            }
                        }

                        if (amountDistributed < incentive && amountDistributed != incentive)
                        {
                            result.Errors.Add($"Cannot distribute the rest of the incentive.  Distributed {amountDistributed} out of {incentive} to the existing {allExistingShifts.Count} shifts");
                        }
                        else
                        {
                            //if we have more to distribute, create new shifts
                            throw new NotImplementedException("Need to implement creating new shifts if more incentives are left to distribute");
                        }
                    }
                }
            }
            catch (ApplicationException ex)
            {
                result = new DistributionResult { Errors = result.Errors };
                result.Errors.Insert(0, ex.Message);
            }
            catch (Exception ex)
            {
                result = new DistributionResult { Errors = result.Errors };
                result.Errors.Insert(0, ex.Message);
            }

            return result;
        }
    }

    public class Employee
    {
        public Employee()
        {
            Shifts = new List<Shift>();
        }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Id { get; set; }
        public List<Shift> Shifts { get; set; }
    }

    public class Shift
    {
        public Shift() { }
        public Shift(DateTime startUtc, double durationInHours)
        {
            Start = startUtc;
            End = Start.AddHours(durationInHours);
        }
        private Guid? _Id;
        public Guid Id
        {
            get
            {
                if (_Id == null)
                {
                    _Id = Guid.NewGuid();
                }

                return _Id.Value;
            }
        }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public decimal HoursWorked
        {
            get
            {
                if (Start == DateTime.MinValue)
                {
                    throw new ApplicationException("There isn't a start set for the shift");
                }
                if (End == DateTime.MinValue)
                {
                    throw new ApplicationException("There isn't a end set for the shift");
                }

                return Convert.ToDecimal((End - Start).TotalHours);
            }
        }
        public int Incentive { get; set; }
        public int DistributedIncentive { get; set; }
        public int AllIncentives { get => Incentive + DistributedIncentive; }
        #region methods        

        public static int GetIncentiveToDistribute(int existingIncentive, decimal amountLeftToDistribute, DistributionRules distributionRules, decimal hours)
        {
            int result = 0;

            if (amountLeftToDistribute == 0 || hours == 0 //nothing to distribute
                || amountLeftToDistribute < distributionRules.MinIncentivePerShift)//The amount to distribute is below the miniumum allowed
            {
                return 0;
            }

            decimal paidHours = distributionRules.CalculatePaidHoursWorked(hours);
            decimal maxPotentialIncentive = distributionRules.GetIncentive(amountLeftToDistribute, paidHours);
            if (maxPotentialIncentive < distributionRules.MinIncentivePerShift)
            {
                return 0;
            }

            if (maxPotentialIncentive > distributionRules.MaxIncentivePerShift)
            {
                maxPotentialIncentive = distributionRules.MaxIncentivePerShift;
            }
            maxPotentialIncentive = maxPotentialIncentive - existingIncentive;
            if (maxPotentialIncentive > distributionRules.MaxIncentivePerShift)
            {
                maxPotentialIncentive = distributionRules.MaxIncentivePerShift;
            }

            if (maxPotentialIncentive <= 0 || maxPotentialIncentive < distributionRules.MinIncentivePerShift)
            {
                maxPotentialIncentive = 0;
            }

            result = Convert.ToInt32(Math.Floor(maxPotentialIncentive));

            return result;
        }

        public void DistributeAndLogIncentive(int incentiveDistributed, HashSet<Guid> shiftIdsDistributed)
        {
            shiftIdsDistributed.Add(Id);
            DistributedIncentive += incentiveDistributed;
        }
        #endregion methods
    }

    public class DistributionRules
    {
        public DistributionRules(int maxIncentivePerShift)
        {
            MaxIncentivePerShift = maxIncentivePerShift;
            IncentiveOptions = new HashSet<decimal>();
        }
        public int MinIncentivePerShift { get; set; }
        public int MaxIncentivePerShift { get; set; }
        HashSet<decimal> IncentiveOptions { get; set; }
        public int BreakDurationInMinutes { get; set; }
        /// <summary>
        /// After the specified hours worked, presume a break is taken.  ie if 5 and someone worked 5hrs, they are presumed to have had a break.  If a 30min break, then the paid hours would be 4.5hours
        /// </summary>
        public decimal BreakAfterHoursWorked { get; set; }

        public decimal GetIncentive(decimal amountLeftToDistribute, decimal hours)
        {
            decimal incentive = amountLeftToDistribute / hours;
            var orderedIncentiveOptions = IncentiveOptions.OrderByDescending(o => o);

            decimal maxIncentive = orderedIncentiveOptions.First(o => o <= incentive);//get the highest incentive option less than or = the incentive

            return maxIncentive;
        }

        public decimal CalculatePaidHoursWorked(decimal hours)
        {
            decimal paidMInutes = hours * 60;
            if (BreakAfterHoursWorked > 0 && BreakDurationInMinutes > 0)
            {
                decimal breakAfterMinutesWorked = BreakAfterHoursWorked * 60;
                if(paidMInutes >= breakAfterMinutesWorked)
                {
                    paidMInutes = paidMInutes - breakAfterMinutesWorked;
                }
            }

            decimal paidHours = paidMInutes / 60;

            return paidHours;
        }
    }

    public class DistributionResult
    {
        public DistributionResult()
        {
            ShiftResult = new List<Shift>();
            Errors = new List<string>();
        }
        List<Shift> ShiftResult { get; set; }
        public List<string> Errors { get; set; }
    }
}
