/*  This file is part of Chummer5a.
 *
 *  Chummer5a is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Chummer5a is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Chummer5a.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  You can obtain the full source code for Chummer5a at
 *  https://github.com/chummer5a/chummer5a
 */
 using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using Chummer.Backend.Equipment;
using System.Text;
using System.Globalization;

namespace Chummer
{
    public partial class frmSelectGear : Form
    {
        private bool _blnLoading = true;
        private string _strSelectedGear = string.Empty;
        private int _intSelectedRating = 0;
        private decimal _decSelectedQty = 1;
        private decimal _decMarkup = 0;

        private int _intAvailModifier = 0;
        private int _intCostMultiplier = 1;

        private string _strAllowedCategories = string.Empty;
        private readonly XmlNode _objParentNode = null;
        private decimal _decMaximumCapacity = -1;
        private bool _blnAddAgain = false;
        private static string s_StrSelectCategory = string.Empty;
        private bool _blnShowPositiveCapacityOnly = false;
        private bool _blnShowNegativeCapacityOnly = false;
        private bool _blnShowArmorCapacityOnly = false;
        private bool _blnBlackMarketDiscount;
        private CapacityStyle _eCapacityStyle = CapacityStyle.Standard;

        private readonly XmlDocument _objXmlDocument = null;
        private readonly Character _objCharacter;

        private readonly List<ListItem> _lstCategory = new List<ListItem>();
        private readonly List<string> _blackMarketMaps = new List<string>();

        #region Control Events
        public frmSelectGear(Character objCharacter, int intAvailModifier = 0, int intCostMultiplier = 1, XmlNode objParentNode = null)
        {
            InitializeComponent();
            LanguageManager.TranslateWinForm(GlobalOptions.Language, this);
            lblMarkupLabel.Visible = objCharacter.Created;
            nudMarkup.Visible = objCharacter.Created;
            lblMarkupPercentLabel.Visible = objCharacter.Created;
            _intAvailModifier = intAvailModifier;
            _intCostMultiplier = intCostMultiplier;
            _objCharacter = objCharacter;
            _objParentNode = objParentNode;
            // Stack Checkbox is only available in Career Mode.
            if (!_objCharacter.Created)
            {
                chkStack.Checked = false;
                chkStack.Visible = false;
            }

            MoveControls();
            // Load the Gear information.
            _objXmlDocument = XmlManager.Load("gear.xml");
            CommonFunctions.GenerateBlackMarketMappings(_objCharacter, _objXmlDocument, _blackMarketMaps);
        }

        private void frmSelectGear_Load(object sender, EventArgs e)
        {
            foreach (Label objLabel in Controls.OfType<Label>())
            {
                if (objLabel.Text.StartsWith('['))
                    objLabel.Text = string.Empty;
            }
            if (_objCharacter.Created)
            {
                chkHideOverAvailLimit.Visible = false;
                chkHideOverAvailLimit.Checked = false;
            }
            else
            {
                chkHideOverAvailLimit.Text = chkHideOverAvailLimit.Text.Replace("{0}", _objCharacter.MaximumAvailability.ToString());
                chkHideOverAvailLimit.Checked = _objCharacter.Options.HideItemsOverAvailLimit;
            }

            XmlNodeList objXmlCategoryList;

            // Populate the Gear Category list.
            if (!string.IsNullOrEmpty(_strAllowedCategories))
            {
                _strAllowedCategories = _strAllowedCategories.TrimEnd(',');
                string[] strAllowed = _strAllowedCategories.Split(',');
                StringBuilder strMount = new StringBuilder();
                foreach (string strAllowedMount in strAllowed)
                {
                    if (!string.IsNullOrEmpty(strAllowedMount))
                        strMount.Append(". = \"" + strAllowedMount + "\" or ");
                }
                strMount.Append("category = \"General\"");
                objXmlCategoryList = _objXmlDocument.SelectNodes("/chummer/categories/category[" + strMount.ToString() + "]");
            }
            else
            {
                objXmlCategoryList = _objXmlDocument.SelectNodes("/chummer/categories/category");
            }

            foreach (XmlNode objXmlCategory in objXmlCategoryList)
            {
                string strCategory = objXmlCategory.InnerText;
                // Make sure the Category isn't in the exclusion list.
                if (objXmlCategory.Attributes["show"]?.InnerText == bool.FalseString)
                {
                    if (!_strAllowedCategories.Split(',').Contains(strCategory))
                        continue;
                }
                if (!_lstCategory.Select(x => x.Value).Contains(strCategory) && RefreshList(strCategory, false, true).Count > 0)
                {
                    string strInnerText = strCategory;
                    _lstCategory.Add(new ListItem(strInnerText, objXmlCategory.Attributes?["translate"]?.InnerText ?? strCategory));
                }
            }
            
            _lstCategory.Sort(CompareListItems.CompareNames);

            if (_lstCategory.Count > 0)
            {
                _lstCategory.Insert(0, new ListItem("Show All", LanguageManager.GetString("String_ShowAll", GlobalOptions.Language)));
            }

            cboCategory.BeginUpdate();
            cboCategory.ValueMember = "Value";
            cboCategory.DisplayMember = "Name";
            cboCategory.DataSource = _lstCategory;

            chkBlackMarketDiscount.Visible = _objCharacter.BlackMarketDiscount;

            cboCategory.EndUpdate();

            if (!string.IsNullOrEmpty(DefaultSearchText))
            {
                txtSearch.Text = DefaultSearchText;
                txtSearch.Enabled = false;
            }

            _blnLoading = false;
            // Select the first Category in the list.
            if (!string.IsNullOrEmpty(s_StrSelectCategory))
                cboCategory.SelectedValue = s_StrSelectCategory;
            if (cboCategory.SelectedIndex == -1)
                cboCategory.SelectedIndex = 0;
            else
                RefreshList(cboCategory.SelectedValue?.ToString());

            if (!string.IsNullOrEmpty(_strSelectedGear))
                lstGear.SelectedValue = _strSelectedGear;
        }

        private void cboCategory_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;

            string strSelectedCategory = cboCategory.SelectedValue?.ToString();

            // Show the Do It Yourself CheckBox if the Commlink Upgrade category is selected.
            if (strSelectedCategory == "Commlink Upgrade")
                chkDoItYourself.Visible = true;
            else
            {
                chkDoItYourself.Visible = false;
                chkDoItYourself.Checked = false;
            }

            RefreshList(strSelectedCategory);
        }

        private void lstGear_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;

            string strSelectedId = lstGear.SelectedValue?.ToString();
            if (!string.IsNullOrEmpty(strSelectedId))
            {
                // Retireve the information for the selected piece of Gear.
                XmlNode objXmlGear = _objXmlDocument.SelectSingleNode("/chummer/gears/gear[id = \"" + strSelectedId + "\"]");

                if (objXmlGear != null)
                {
                    string strName = objXmlGear["name"]?.InnerText ?? string.Empty;
                    // If a Grenade is selected, show the Aerodynamic checkbox.
                    if (strName.StartsWith("Grenade:"))
                        chkAerodynamic.Visible = true;
                    else
                    {
                        chkAerodynamic.Visible = false;
                        chkAerodynamic.Checked = false;
                    }

                    // Quantity.
                    nudGearQty.Enabled = true;
                    nudGearQty.Minimum = 1;
                    string strCostFor = objXmlGear["costfor"]?.InnerText;
                    if (!string.IsNullOrEmpty(strCostFor))
                    {
                        nudGearQty.Value = Convert.ToDecimal(strCostFor, GlobalOptions.InvariantCultureInfo);
                        nudGearQty.Increment = Convert.ToDecimal(strCostFor, GlobalOptions.InvariantCultureInfo);
                    }
                    else
                    {
                        nudGearQty.Value = 1;
                        nudGearQty.Increment = 1;
                    }
                    if (strName.StartsWith("Nuyen"))
                    {
                        int intDecimalPlaces = _objCharacter.Options.NuyenFormat.Length - 1 - _objCharacter.Options.NuyenFormat.LastIndexOf('.');
                        if (intDecimalPlaces <= 0)
                        {
                            nudGearQty.DecimalPlaces = 0;
                            nudGearQty.Minimum = 1.0m;
                        }
                        else
                        {
                            nudGearQty.DecimalPlaces = intDecimalPlaces;
                            decimal decMinimum = 1.0m;
                            // Need a for loop instead of a power system to maintain exact precision
                            for (int i = 0; i < intDecimalPlaces; ++i)
                                decMinimum /= 10.0m;
                            nudGearQty.Minimum = decMinimum;
                        }
                    }
                    else if (objXmlGear["category"].InnerText == "Currency")
                    {
                        nudGearQty.DecimalPlaces = 2;
                        nudGearQty.Minimum = 0.01m;
                    }
                    else
                    {
                        nudGearQty.DecimalPlaces = 0;
                        nudGearQty.Minimum = 1.0m;
                    }
                }
                else
                {
                    nudGearQty.Enabled = false;
                    nudGearQty.Value = 1;
                    chkAerodynamic.Visible = false;
                    chkAerodynamic.Checked = false;
                }
            }
            else
            {
                nudGearQty.Enabled = false;
                nudGearQty.Value = 1;
                chkAerodynamic.Visible = false;
                chkAerodynamic.Checked = false;
            }

            UpdateGearInfo();
        }

        private void nudRating_ValueChanged(object sender, EventArgs e)
        {
            UpdateGearInfo();
        }

        private void chkBlackMarketDiscount_CheckedChanged(object sender, EventArgs e)
        {
            if (chkShowOnlyAffordItems.Checked)
            {
                RefreshList(cboCategory.SelectedValue?.ToString());
            }
            UpdateGearInfo();
        }

        private void cmdOK_Click(object sender, EventArgs e)
        {
            AcceptForm();
        }

        private void cmdCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            RefreshList(cboCategory.SelectedValue?.ToString());
        }

        private void lstGear_DoubleClick(object sender, EventArgs e)
        {
            AcceptForm();
        }

        private void cmdOKAdd_Click(object sender, EventArgs e)
        {
            _blnAddAgain = true;
            cmdOK_Click(sender, e);
        }

        private void nudGearQty_ValueChanged(object sender, EventArgs e)
        {
            UpdateGearInfo();
        }

        private void chkFreeItem_CheckedChanged(object sender, EventArgs e)
        {
            if (chkShowOnlyAffordItems.Checked)
            {
                RefreshList(cboCategory.SelectedValue?.ToString());
            }
            UpdateGearInfo();
        }

        private void chkDoItYourself_CheckedChanged(object sender, EventArgs e)
        {
            if (chkShowOnlyAffordItems.Checked)
            {
                RefreshList(cboCategory.SelectedValue?.ToString());
            }
            UpdateGearInfo();
        }

        private void nudMarkup_ValueChanged(object sender, EventArgs e)
        {
            if (chkShowOnlyAffordItems.Checked)
            {
                RefreshList(cboCategory.SelectedValue?.ToString());
            }
            UpdateGearInfo();
        }

        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down)
            {
                if (lstGear.SelectedIndex + 1 < lstGear.Items.Count)
                {
                    lstGear.SelectedIndex++;
                }
                else if (lstGear.Items.Count > 0)
                {
                    lstGear.SelectedIndex = 0;
                }
            }
            if (e.KeyCode == Keys.Up)
            {
                if (lstGear.SelectedIndex - 1 >= 0)
                {
                    lstGear.SelectedIndex--;
                }
                else if (lstGear.Items.Count > 0)
                {
                    lstGear.SelectedIndex = lstGear.Items.Count - 1;
                }
            }
        }

        private void txtSearch_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up)
                txtSearch.Select(txtSearch.Text.Length, 0);
        }
        #endregion

        #region Properties
        /// <summary>
        /// Whether or not the user wants to add another item after this one.
        /// </summary>
        public bool AddAgain
        {
            get
            {
                return _blnAddAgain;
            }
        }

        /// <summary>
        /// Only items that grant Capacity should be shown.
        /// </summary>
        public bool ShowPositiveCapacityOnly
        {
            set
            {
                _blnShowPositiveCapacityOnly = value;
                if (value)
                    _blnShowNegativeCapacityOnly = false;
            }
        }

        /// <summary>
        /// Only items that consume Capacity should be shown.
        /// </summary>
        public bool ShowNegativeCapacityOnly
        {
            set
            {
                _blnShowNegativeCapacityOnly = value;
                if (value)
                    _blnShowPositiveCapacityOnly = false;
            }
        }

        /// <summary>
        /// Only items that consume Armor Capacity should be shown.
        /// </summary>
        public bool ShowArmorCapacityOnly
        {
            set
            {
                _blnShowArmorCapacityOnly = value;
            }
        }

        /// <summary>
        /// Name of Gear that was selected in the dialogue.
        /// </summary>
        public string SelectedGear
        {
            get
            {
                return _strSelectedGear;
            }
            set
            {
                _strSelectedGear = value;
            }
        }

        /// <summary>
        /// Rating that was selected in the dialogue.
        /// </summary>
        public int SelectedRating
        {
            get
            {
                return _intSelectedRating;
            }
        }

        /// <summary>
        /// Quantity that was selected in the dialogue.
        /// </summary>
        public decimal SelectedQty
        {
            get
            {
                return _decSelectedQty;
            }
        }

        /// <summary>
        /// Set the maximum Capacity the piece of Gear is allowed to be.
        /// </summary>
        public decimal MaximumCapacity
        {
            set
            {
                _decMaximumCapacity = value;
                lblMaximumCapacity.Text = LanguageManager.GetString("Label_MaximumCapacityAllowed", GlobalOptions.Language) + ' ' + _decMaximumCapacity.ToString("#,0.##", GlobalOptions.CultureInfo);
            }
        }

        /// <summary>
        /// Categories that the Gear allows to be used.
        /// </summary>
        public string AllowedCategories
        {
            get
            {
                return _strAllowedCategories;
            }
            set
            {
                _strAllowedCategories = value;
            }
        }

        /// <summary>
        /// Whether or not the item should be added for free.
        /// </summary>
        public bool FreeCost
        {
            get
            {
                return chkFreeItem.Checked;
            }
        }

        /// <summary>
        /// Whether or not the item's cost should be cut in half for being a Do It Yourself component/upgrade.
        /// </summary>
        public bool DoItYourself
        {
            get
            {
                return chkDoItYourself.Checked;
            }
        }

        /// <summary>
        /// Markup percentage.
        /// </summary>
        public decimal Markup
        {
            get
            {
                return _decMarkup;
            }
        }

        /// <summary>
        /// Whether or not the Gear should stack with others if possible.
        /// </summary>
        public bool Stack
        {
            get
            {
                return chkStack.Checked;
            }
        }

        /// <summary>
        /// Whether or not the Stack Checkbox should be shown (default true).
        /// </summary>
        public bool EnableStack
        {
            set
            {
                chkStack.Visible = value;
                if (!value)
                    chkStack.Checked = false;
            }
        }

        /// <summary>
        /// Capacity display style.
        /// </summary>
        public CapacityStyle CapacityDisplayStyle
        {
            set
            {
                _eCapacityStyle = value;
            }
        }

        /// <summary>
        /// Whether or not a Grenade is Aerodynamic.
        /// </summary>
        public bool Aerodynamic
        {
            get
            {
                return chkAerodynamic.Checked;
            }
        }

        /// <summary>
        /// Whether or not the selected Vehicle is used.
        /// </summary>
        public bool BlackMarketDiscount
        {
            get
            {
                return _blnBlackMarketDiscount;
            }
        }

        /// <summary>
        /// Default text string to filter by.
        /// </summary>
        public string DefaultSearchText { get; set; }
        #endregion

        #region Methods
        private static readonly char[] lstBracketChars = { '[', ']' };
        /// <summary>
        /// Update the Gear's information based on the Gear selected and current Rating.
        /// </summary>
        private void UpdateGearInfo()
        {
            string strSelectedId = lstGear.SelectedValue?.ToString();
            if (_blnLoading || string.IsNullOrEmpty(strSelectedId))
            {
                lblGearDeviceRating.Text = string.Empty;
                lblSource.Text = string.Empty;
                lblAvail.Text = string.Empty;
                lblCost.Text = string.Empty;
                chkBlackMarketDiscount.Checked = false;
                lblTest.Text = string.Empty;
                lblCapacity.Text = string.Empty;
                nudRating.Minimum = 0;
                nudRating.Maximum = 0;
                nudRating.Enabled = false;
                tipTooltip.SetToolTip(lblSource, string.Empty);
                return;
            }

            // Retireve the information for the selected piece of Gear.
            XmlNode objXmlGear = _objXmlDocument.SelectSingleNode("/chummer/gears/gear[id = \"" + strSelectedId + "\"]");

            if (objXmlGear == null)
            {
                lblGearDeviceRating.Text = string.Empty;
                lblSource.Text = string.Empty;
                lblAvail.Text = string.Empty;
                lblCost.Text = string.Empty;
                chkBlackMarketDiscount.Checked = false;
                lblTest.Text = string.Empty;
                lblCapacity.Text = string.Empty;
                nudRating.Minimum = 0;
                nudRating.Maximum = 0;
                nudRating.Enabled = false;
                tipTooltip.SetToolTip(lblSource, string.Empty);
                return;
            }

            // Retireve the information for the selected piece of Cyberware.
            decimal decItemCost = 0.0m;

            lblGearDeviceRating.Text = objXmlGear["devicerating"]?.InnerText ?? string.Empty;

            string strBook = CommonFunctions.LanguageBookShort(objXmlGear["source"].InnerText, GlobalOptions.Language);
            string strPage = objXmlGear["altpage"]?.InnerText ?? objXmlGear["page"].InnerText;
            lblSource.Text = strBook + ' ' + strPage;

            // Extract the Avil and Cost values from the Gear info since these may contain formulas and/or be based off of the Rating.
            // This is done using XPathExpression.

            // Avail.
            // If avail contains "F" or "R", remove it from the string so we can use the expression.
            string strAvail = string.Empty;
            string strAvailExpr = string.Empty;
            string strPrefix = string.Empty;
            XmlNode objAvailNode = objXmlGear["avail"];
            if (objAvailNode == null)
            {
                int intHighestAvailNode = 0;
                foreach (XmlNode objLoopNode in objXmlGear.ChildNodes)
                {
                    if (objLoopNode.NodeType == XmlNodeType.Element && objLoopNode.Name.StartsWith("avail"))
                    {
                        string strLoopCostString = objLoopNode.Name.Substring(4);
                        if (int.TryParse(strLoopCostString, out int intTmp))
                        {
                            intHighestAvailNode = Math.Max(intHighestAvailNode, intTmp);
                        }
                    }
                }
                objAvailNode = objXmlGear["avail" + intHighestAvailNode];
                for (int i = decimal.ToInt32(nudRating.Value); i <= intHighestAvailNode; ++i)
                {
                    XmlNode objLoopNode = objXmlGear["avail" + i.ToString(GlobalOptions.InvariantCultureInfo)];
                    if (objLoopNode != null)
                    {
                        objAvailNode = objLoopNode;
                        break;
                    }
                }
            }
            strAvailExpr = objAvailNode.InnerText;

            if (!string.IsNullOrEmpty(strAvailExpr))
            {
                char chrLastChar = strAvailExpr[strAvailExpr.Length - 1];
                if (chrLastChar == 'R')
                {
                    strAvail = LanguageManager.GetString("String_AvailRestricted", GlobalOptions.Language);
                    // Remove the trailing character if it is "F" or "R".
                    strAvailExpr = strAvailExpr.Substring(0, strAvailExpr.Length - 1);
                }
                else if (chrLastChar == 'F')
                {
                    strAvail = LanguageManager.GetString("String_AvailForbidden", GlobalOptions.Language);
                    // Remove the trailing character if it is "F" or "R".
                    strAvailExpr = strAvailExpr.Substring(0, strAvailExpr.Length - 1);
                }
                if (strAvailExpr[0] == '+')
                {
                    strPrefix = "+";
                    strAvailExpr = strAvailExpr.Substring(1, strAvailExpr.Length - 1);
                }
            }

            try
            {
                lblAvail.Text = strPrefix + (Convert.ToInt32(CommonFunctions.EvaluateInvariantXPath(strAvailExpr.Replace("Rating", nudRating.Value.ToString(GlobalOptions.InvariantCultureInfo)))) + _intAvailModifier).ToString() + strAvail;
            }
            catch (XPathException)
            {
                lblAvail.Text = strPrefix + strAvailExpr + strAvail;
            }

            decimal decMultiplier = nudGearQty.Value / nudGearQty.Increment;
            if (chkDoItYourself.Checked)
                decMultiplier *= 0.5m;

            // Cost.
            if (_blackMarketMaps != null)
                chkBlackMarketDiscount.Checked =
                    _blackMarketMaps.Contains(objXmlGear["category"]?.InnerText);

            if (chkFreeItem.Checked)
            {
                lblCost.Text = 0.ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + '¥';
                decItemCost = 0;
            }
            else
            {
                XmlNode objCostNode = objXmlGear["cost"];
                if (objCostNode == null)
                {
                    int intHighestCostNode = 0;
                    foreach (XmlNode objLoopNode in objXmlGear.ChildNodes)
                    {
                        if (objLoopNode.NodeType == XmlNodeType.Element && objLoopNode.Name.StartsWith("cost"))
                        {
                            string strLoopCostString = objLoopNode.Name.Substring(4);
                            if (int.TryParse(strLoopCostString, out int intTmp))
                            {
                                intHighestCostNode = Math.Max(intHighestCostNode, intTmp);
                            }
                        }
                    }
                    objCostNode = objXmlGear["cost" + intHighestCostNode];
                    for (int i = decimal.ToInt32(nudRating.Value); i <= intHighestCostNode; ++i)
                    {
                        XmlNode objLoopNode = objXmlGear["cost" + i.ToString(GlobalOptions.InvariantCultureInfo)];
                        if (objLoopNode != null)
                        {
                            objCostNode = objLoopNode;
                            break;
                        }
                    }
                }
                if (objCostNode != null)
                {
                    try
                    {
                        decimal decCost = Convert.ToDecimal(CommonFunctions.EvaluateInvariantXPath(objCostNode.InnerText.Replace("Rating", nudRating.Value.ToString(GlobalOptions.InvariantCultureInfo))), GlobalOptions.InvariantCultureInfo) * decMultiplier;
                        decCost *= 1 + (nudMarkup.Value / 100.0m);
                        if (chkBlackMarketDiscount.Checked)
                            decCost *= 0.9m;
                        lblCost.Text = (decCost * _intCostMultiplier).ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + '¥';
                        decItemCost = decCost;
                    }
                    catch (XPathException)
                    {
                        lblCost.Text = objCostNode.InnerText;
                        if (decimal.TryParse(objCostNode.InnerText, NumberStyles.Any, GlobalOptions.InvariantCultureInfo, out decimal decTemp))
                        {
                            decItemCost = decTemp;
                            lblCost.Text = (decItemCost * _intCostMultiplier).ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + '¥';
                        }
                    }

                    if (objCostNode.InnerText.StartsWith("FixedValues("))
                    {
                        string[] strValues = objCostNode.InnerText.TrimStart("FixedValues(", true).TrimEnd(')').Split(',');
                        string strCost = "0";
                        if (nudRating.Value > 0)
                            strCost = strValues[decimal.ToInt32(nudRating.Value) - 1].Trim(lstBracketChars);
                        decimal decCost = Convert.ToDecimal(strCost, GlobalOptions.InvariantCultureInfo) * decMultiplier;
                        decCost *= 1 + (nudMarkup.Value / 100.0m);
                        if (chkBlackMarketDiscount.Checked)
                            decCost *= 0.9m;
                        lblCost.Text = (decCost * _intCostMultiplier).ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + "¥+";
                        decItemCost = decCost;
                    }
                    else if (objCostNode.InnerText.StartsWith("Variable("))
                    {
                        decimal decMin = 0;
                        decimal decMax = decimal.MaxValue;
                        string strCost = objCostNode.InnerText.TrimStart("Variable(", true).TrimEnd(')');
                        if (strCost.Contains('-'))
                        {
                            string[] strValues = strCost.Split('-');
                            decMin = Convert.ToDecimal(strValues[0], GlobalOptions.InvariantCultureInfo);
                            decMax = Convert.ToDecimal(strValues[1], GlobalOptions.InvariantCultureInfo);
                        }
                        else
                            decMin = Convert.ToDecimal(strCost.FastEscape('+'), GlobalOptions.InvariantCultureInfo);

                        if (decMax == decimal.MaxValue)
                            lblCost.Text = decMin.ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + "¥+";
                        else
                            lblCost.Text = decMin.ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + " - " + decMax.ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + '¥';

                        decItemCost = decMin;
                    }
                }
            }

            // Update the Avail Test Label.
            lblTest.Text = _objCharacter.AvailTest(decItemCost * _intCostMultiplier, lblAvail.Text);

            // Capacity.
            // XPathExpression cannot evaluate while there are square brackets, so remove them if necessary.
            string strCapacity = "0";
            string strCapacityField = "capacity";
            if (_blnShowArmorCapacityOnly)
                strCapacityField = "armorcapacity";
            bool blnSquareBrackets = false;

            if (_eCapacityStyle == CapacityStyle.Zero)
                lblCapacity.Text = "[0]";
            else
            {
                string strCapacityText = objXmlGear[strCapacityField]?.InnerText;
                if (!string.IsNullOrEmpty(strCapacityText))
                {
                    if (strCapacityText.Contains("/["))
                    {
                        int intPos = strCapacityText.IndexOf("/[");
                        string strFirstHalf = strCapacityText.Substring(0, intPos);
                        string strSecondHalf = strCapacityText.Substring(intPos + 1, strCapacityText.Length - intPos - 1);

                        if (strFirstHalf == "[*]")
                            lblCapacity.Text = "*";
                        else
                        {
                            blnSquareBrackets = strFirstHalf.Contains('[');
                            strCapacity = strFirstHalf;
                            if (blnSquareBrackets && strCapacity.Length > 2)
                                strCapacity = strCapacity.Substring(1, strCapacity.Length - 2);

                            if (strCapacity.StartsWith("FixedValues("))
                            {
                                string[] strValues = strCapacity.TrimStart("FixedValues(", true).TrimEnd(')').Split(',');
                                if (strValues.Length >= decimal.ToInt32(nudRating.Value))
                                    lblCapacity.Text = strValues[decimal.ToInt32(nudRating.Value) - 1];
                                else
                                {
                                    try
                                    {
                                        lblCapacity.Text = ((double)CommonFunctions.EvaluateInvariantXPath(strCapacity.Replace("Rating", nudRating.Value.ToString(GlobalOptions.InvariantCultureInfo)))).ToString("#,0.##", GlobalOptions.CultureInfo);
                                    }
                                    catch (XPathException)
                                    {
                                        lblCapacity.Text = strCapacity;
                                    }
                                    catch (OverflowException) // Result is text and not a double
                                    {
                                        lblCapacity.Text = strCapacity;
                                    }
                                    catch (InvalidCastException) // Result is text and not a double
                                    {
                                        lblCapacity.Text = strCapacity;
                                    }
                                }
                            }
                            else
                            {
                                try
                                {
                                    lblCapacity.Text = ((double)CommonFunctions.EvaluateInvariantXPath(strCapacity.Replace("Rating", nudRating.Value.ToString(GlobalOptions.InvariantCultureInfo)))).ToString("#,0.##", GlobalOptions.CultureInfo);
                                }
                                catch (XPathException)
                                {
                                    lblCapacity.Text = strCapacity;
                                }
                                catch (OverflowException) // Result is text and not a double
                                {
                                    lblCapacity.Text = strCapacity;
                                }
                                catch (InvalidCastException) // Result is text and not a double
                                {
                                    lblCapacity.Text = strCapacity;
                                }
                            }

                            if (blnSquareBrackets)
                                lblCapacity.Text = '[' + lblCapacity.Text + ']';
                        }

                        lblCapacity.Text += '/' + strSecondHalf;
                    }
                    else if (strCapacityText == "[*]")
                        lblCapacity.Text = "*";
                    else
                    {
                        blnSquareBrackets = strCapacityText.Contains('[');
                        strCapacity = strCapacityText;
                        if (blnSquareBrackets && strCapacity.Length > 2)
                            strCapacity = strCapacity.Substring(1, strCapacity.Length - 2);
                        if (strCapacityText.StartsWith("FixedValues("))
                        {
                            string[] strValues = strCapacityText.TrimStart("FixedValues(", true).TrimEnd(')').Split(',');
                            if (strValues.Length >= decimal.ToInt32(nudRating.Value))
                                lblCapacity.Text = strValues[decimal.ToInt32(nudRating.Value) - 1];
                            else
                                lblCapacity.Text = "0";
                        }
                        else
                        {
                            string strCalculatedCapacity = string.Empty;
                            try
                            {
                                strCalculatedCapacity = ((double)CommonFunctions.EvaluateInvariantXPath(strCapacity.Replace("Rating", nudRating.Value.ToString(GlobalOptions.InvariantCultureInfo)))).ToString("#,0.##", GlobalOptions.CultureInfo);
                            }
                            catch (XPathException)
                            {
                                lblCapacity.Text = strCapacity;
                            }
                            catch (OverflowException) // Result is text and not a double
                            {
                                lblCapacity.Text = strCapacity;
                            }
                            catch (InvalidCastException) // Result is text and not a double
                            {
                                lblCapacity.Text = strCapacity;
                            }
                            if (!string.IsNullOrEmpty(strCalculatedCapacity))
                                lblCapacity.Text = strCalculatedCapacity;
                        }
                        if (blnSquareBrackets)
                            lblCapacity.Text = '[' + lblCapacity.Text + ']';
                    }
                }
                else
                {
                    lblCapacity.Text = "0";
                }
            }

            // Rating.
            if (Convert.ToInt32(objXmlGear["rating"].InnerText) > 0)
            {
                nudRating.Maximum = Convert.ToInt32(objXmlGear["rating"].InnerText);
                if (objXmlGear["minrating"] != null)
                {
                    decimal decOldMinimum = nudRating.Minimum;
                    nudRating.Minimum = Convert.ToInt32(objXmlGear["minrating"].InnerText);
                    if (decOldMinimum > nudRating.Minimum)
                    {
                        nudRating.Value -= decOldMinimum - nudRating.Minimum;
                    }
                }
                else
                {
                    nudRating.Minimum = 1;
                }
                if (chkHideOverAvailLimit.Checked)
                {
                    while (nudRating.Maximum > nudRating.Minimum && !Backend.SelectionShared.CheckAvailRestriction(objXmlGear, _objCharacter, decimal.ToInt32(nudRating.Maximum), _intAvailModifier))
                    {
                        nudRating.Maximum -= 1;
                    }
                }

                if (nudRating.Minimum == nudRating.Maximum)
                    nudRating.Enabled = false;
                else
                    nudRating.Enabled = true;
            }
            else
            {
                nudRating.Minimum = 0;
                nudRating.Maximum = 0;
                nudRating.Enabled = false;
            }

            tipTooltip.SetToolTip(lblSource, CommonFunctions.LanguageBookLong(objXmlGear["source"].InnerText, GlobalOptions.Language) + ' ' + LanguageManager.GetString("String_Page", GlobalOptions.Language) + ' ' + strPage);
        }

        private IList<ListItem> RefreshList(string strCategory, bool blnDoUIUpdate = true, bool blnTerminateAfterFirst = false)
        {
            string strFilter = "(" + _objCharacter.Options.BookXPath() + ')';
            if (!string.IsNullOrEmpty(strCategory) && strCategory != "Show All" && (_objCharacter.Options.SearchInCategoryOnly || txtSearch.TextLength == 0))
                strFilter += " and category = \"" + strCategory + '\"';
            else if (!string.IsNullOrEmpty(_strAllowedCategories))
            {
                StringBuilder objCategoryFilter = new StringBuilder();
                foreach (string strItem in _lstCategory.Select(x => x.Value))
                {
                    if (!string.IsNullOrEmpty(strItem))
                        objCategoryFilter.Append("category = \"" + strItem + "\" or ");
                }
                if (objCategoryFilter.Length > 0)
                {
                    strFilter += " and (" + objCategoryFilter.ToString().TrimEnd(" or ") + ')';
                }
            }
            if (txtSearch.TextLength != 0)
            {
                // Treat everything as being uppercase so the search is case-insensitive.
                string strSearchText = txtSearch.Text.ToUpper();
                strFilter += " and ((contains(translate(name,'abcdefghijklmnopqrstuvwxyzàáâãäåçèéêëìíîïñòóôõöùúûüýß','ABCDEFGHIJKLMNOPQRSTUVWXYZÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜÝß'), \"" + strSearchText + "\") and not(translate)) or contains(translate(translate,'abcdefghijklmnopqrstuvwxyzàáâãäåçèéêëìíîïñòóôõöùúûüýß','ABCDEFGHIJKLMNOPQRSTUVWXYZÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜÝß'), \"" + strSearchText + "\"))";
            }
            if (_blnShowArmorCapacityOnly)
                strFilter += " and contains(armorcapacity, \"[\")";
            else if (_blnShowPositiveCapacityOnly)
                strFilter += " and not(contains(capacity, \"[\"))";
            else if (_blnShowNegativeCapacityOnly)
                strFilter += " and contains(capacity, \"[\")";
            if (_objParentNode == null)
                strFilter += " and not(requireparent)";

            return BuildGearList(_objXmlDocument.SelectNodes("/chummer/gears/gear[" + strFilter + "]"), blnDoUIUpdate, blnTerminateAfterFirst);
        }

        private IList<ListItem> BuildGearList(XmlNodeList objXmlGearList, bool blnDoUIUpdate = true, bool blnTerminateAfterFirst = false)
        {
            List<ListItem> lstGears = new List<ListItem>();
            foreach (XmlNode objXmlGear in objXmlGearList)
            {
                XmlNode xmlTestNode = objXmlGear.SelectSingleNode("forbidden/parentdetails");
                if (xmlTestNode != null)
                {
                    // Assumes topmost parent is an AND node
                    if (_objParentNode.ProcessFilterOperationNode(xmlTestNode, false))
                    {
                        continue;
                    }
                }
                xmlTestNode = objXmlGear.SelectSingleNode("required/parentdetails");
                if (xmlTestNode != null)
                {
                    // Assumes topmost parent is an AND node
                    if (!_objParentNode.ProcessFilterOperationNode(xmlTestNode, false))
                    {
                        continue;
                    }
                }
                xmlTestNode = objXmlGear.SelectSingleNode("forbidden/geardetails");
                if (xmlTestNode != null)
                {
                    // Assumes topmost parent is an AND node
                    if (_objParentNode.ProcessFilterOperationNode(xmlTestNode, false))
                    {
                        continue;
                    }
                }
                xmlTestNode = objXmlGear.SelectSingleNode("required/geardetails");
                if (xmlTestNode != null)
                {
                    // Assumes topmost parent is an AND node
                    if (!_objParentNode.ProcessFilterOperationNode(xmlTestNode, false))
                    {
                        continue;
                    }
                }

                decimal decCostMultiplier = nudGearQty.Value / nudGearQty.Increment;
                if (chkDoItYourself.Checked)
                    decCostMultiplier *= 0.5m;
                decCostMultiplier *= 1 + (nudMarkup.Value / 100.0m);
                if (chkBlackMarketDiscount.Checked)
                    decCostMultiplier *= 0.9m;
                if (!blnDoUIUpdate ||
                    ((!chkHideOverAvailLimit.Checked || Backend.SelectionShared.CheckAvailRestriction(objXmlGear, _objCharacter, 1, _intAvailModifier) &&
                    (chkFreeItem.Checked || !chkShowOnlyAffordItems.Checked ||
                    Backend.SelectionShared.CheckNuyenRestriction(objXmlGear, _objCharacter, _objCharacter.Nuyen, decCostMultiplier)))))
                {
                    string strDisplayName = objXmlGear["translate"]?.InnerText ?? objXmlGear["name"].InnerText;

                    if (!_objCharacter.Options.SearchInCategoryOnly && txtSearch.TextLength != 0)
                    {
                        string strCategory = objXmlGear["category"]?.InnerText;
                        if (!string.IsNullOrEmpty(strCategory))
                        {
                            ListItem objFoundItem = _lstCategory.Find(objFind => objFind.Value == strCategory);
                            if (!string.IsNullOrEmpty(objFoundItem.Name))
                                strDisplayName += " [" + objFoundItem.Name + "]";
                        }
                    }
                    // When searching, Category needs to be added to the Value so we can identify the English Category name.
                    lstGears.Add(new ListItem(objXmlGear["id"].InnerText, strDisplayName));
                    if (blnTerminateAfterFirst)
                        break;
                }
            }
            if (blnDoUIUpdate)
            {
                lstGears.Sort(CompareListItems.CompareNames);
                lstGear.BeginUpdate();
                string strOldSelected = lstGear.SelectedValue?.ToString();
                bool blnOldLoading = _blnLoading;
                _blnLoading = true;
                lstGear.ValueMember = "Value";
                lstGear.DisplayMember = "Name";
                lstGear.DataSource = lstGears;
                _blnLoading = blnOldLoading;
                if (string.IsNullOrEmpty(strOldSelected))
                    lstGear.SelectedIndex = -1;
                else
                    lstGear.SelectedValue = strOldSelected;
                lstGear.EndUpdate();
            }

            return lstGears;
        }

        /// <summary>
        /// Add a Category to the Category list.
        /// </summary>
        public void AddCategory(string strCategories)
        {
            string[] strCategoryList = strCategories.Split(',');
            XmlNode objXmlCategoryList = _objXmlDocument.SelectSingleNode("/chummer/categories");
            foreach (string strCategory in strCategoryList)
            {
                _lstCategory.Add(new ListItem(strCategory, objXmlCategoryList.SelectSingleNode("category[text() = \"" + strCategory + "\"]/@translate")?.InnerText ?? strCategory));
            }
            cboCategory.BeginUpdate();
            cboCategory.DataSource = null;
            cboCategory.ValueMember = "Value";
            cboCategory.DisplayMember = "Name";
            cboCategory.DataSource = _lstCategory;
            cboCategory.EndUpdate();
        }

        /// <summary>
        /// Accept the selected item and close the form.
        /// </summary>
        private void AcceptForm()
        {
            string strSelectedId = lstGear.SelectedValue?.ToString();
            if (!string.IsNullOrEmpty(strSelectedId))
            {
                XmlNode objNode = _objXmlDocument.SelectSingleNode("/chummer/gears/gear[id = \"" + strSelectedId + "\"]");

                if (objNode != null)
                {
                    _strSelectedGear = objNode["name"].InnerText;
                    s_StrSelectCategory = (_objCharacter.Options.SearchInCategoryOnly || txtSearch.TextLength == 0) ? cboCategory.SelectedValue?.ToString() : objNode["category"].InnerText;
                    _blnBlackMarketDiscount = chkBlackMarketDiscount.Checked;
                    _intSelectedRating = decimal.ToInt32(nudRating.Value);
                    _decSelectedQty = nudGearQty.Value;
                    _decMarkup = nudMarkup.Value;

                    DialogResult = DialogResult.OK;
                }
            }
        }

        private void MoveControls()
        {
            int intWidth = Math.Max(lblCapacityLabel.Width, lblAvailLabel.Width);
            intWidth = Math.Max(intWidth, lblCostLabel.Width);
            intWidth = Math.Max(intWidth, lblRatingLabel.Width);
            intWidth = Math.Max(intWidth, lblGearQtyLabel.Width);
            intWidth = Math.Max(intWidth, lblMarkupLabel.Width);

            lblCapacity.Left = lblCapacityLabel.Left + intWidth + 6;
            lblAvail.Left = lblAvailLabel.Left + intWidth + 6;
            lblTestLabel.Left = lblAvail.Left + lblAvail.Width + 16;
            lblTest.Left = lblTestLabel.Left + lblTestLabel.Width + 6;
            lblCost.Left = lblCostLabel.Left + intWidth + 6;
            nudRating.Left = lblRatingLabel.Left + intWidth + 6;
            nudGearQty.Left = lblGearQtyLabel.Left + intWidth + 6;
            chkStack.Left = nudGearQty.Left + nudGearQty.Width + 6;
            nudMarkup.Left = lblMarkupLabel.Left + intWidth + 6;
            lblMarkupPercentLabel.Left = nudMarkup.Left + nudMarkup.Width;

            lblGearDeviceRating.Left = lblGearDeviceRatingLabel.Left + lblGearDeviceRatingLabel.Width + 6;

            chkDoItYourself.Left = chkFreeItem.Left + chkFreeItem.Width + 6;

            lblSearchLabel.Left = txtSearch.Left - 6 - lblSearchLabel.Width;
        }
        #endregion
    }
}
