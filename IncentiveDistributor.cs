﻿using System;
using System.Collections.Generic;
using System.Text;

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
                int amountToDistribute = Shift.GetAmountToDistribute(primaryShiftToDistributeTo.Incentive, incentive, distributionRules);
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
                                int shiftAmountToDistribute = Shift.GetAmountToDistribute(shift.Incentive, amountLeftToDistribute, distributionRules);
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
            catch(ApplicationException ex)
            {
                result = new DistributionResult { Errors = result.Errors };
                result.Errors.Insert(0, ex.Message);
            }
            catch(Exception ex)
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
        public double HoursWorked
        {
            get
            {
                if(Start == DateTime.MinValue)
                {
                    throw new ApplicationException("There isn't a start set for the shift");
                }
                if (End == DateTime.MinValue)
                {
                    throw new ApplicationException("There isn't a end set for the shift");
                }

                return (End - Start).TotalHours;
            }
        }
        public int Incentive { get; set; }
        public int DistributedIncentive { get; set; }
        #region methods        

        public static int GetAmountToDistribute(int existingIncentive, int amountLeftToDistribute, DistributionRules distributionRules)
        {
            int result = 0;

            if (amountLeftToDistribute == 0)
            {
                return 0;
            }
            else if (amountLeftToDistribute < distributionRules.MinIncentivePerShift)
            {
                return 0;//The amount to distribute is below the miniumum allowed
            }

            if (amountLeftToDistribute <= distributionRules.MaxIncentivePerShift && amountLeftToDistribute >= distributionRules.MinIncentivePerShift)
            {
                result = amountLeftToDistribute;
            }
            if(amountLeftToDistribute > distributionRules.MinIncentivePerShift && amountLeftToDistribute > distributionRules.MaxIncentivePerShift)
            {
                int amountToTry = distributionRules.DistributionInterval;
                if(amountToTry == amountLeftToDistribute)
                {
                    result = amountToTry;
                }
                else
                {
                    int tries = 0;
                    do
                    {
                        tries++;
                        if (tries > 1000)//prevent infinite loop
                        {
                            throw new Exception("Tried too many attempts to get the amount to distribute");
                        }
                        amountToTry += distributionRules.DistributionInterval;
                        if (amountToTry > distributionRules.MaxIncentivePerShift || amountToTry > amountLeftToDistribute)
                        {
                            amountToTry -= distributionRules.DistributionInterval;
                            break;
                        }
                    }
                    while (amountToTry < amountLeftToDistribute && amountToTry < distributionRules.MaxIncentivePerShift);
                    result = amountToTry;
                }
            }

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
        }
        public int MinIncentivePerShift { get; set; }
        public int MaxIncentivePerShift { get; set; }
        private int _DistributionInterval = 1;
        /// <summary>
        /// The amount to try to distribute by
        /// </summary>
        public int DistributionInterval
        {
            get
            {                
                return _DistributionInterval;
            }
            set
            {
                if (value > MaxIncentivePerShift)
                {
                    throw new Exception($"The interval cannot be more than the max");
                }
                _DistributionInterval = value;
            }
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