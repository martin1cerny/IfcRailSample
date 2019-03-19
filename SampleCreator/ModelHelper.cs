using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xbim.Common;
using Xbim.Common.Exceptions;
using Xbim.Common.Metadata;
using Xbim.IfcRail;
using Xbim.IfcRail.Kernel;
using Xbim.IfcRail.UtilityResource;
using Xbim.IO;
using Xbim.IO.Memory;

namespace SampleCreator
{
    internal class ModelHelper
    {
        public static MemoryModel GetModel(string path)
        {
            var model = new MemoryModel(new EntityFactoryIfcRailPilot());
            model.LoadStep21(path);
            SetUpOwnerHistory(model);

            return model;
        }

        private static void SetUpOwnerHistory(IModel model)
        {
            var i = model.Instances;
            using (var txn = model.BeginTransaction("Owner history set up"))
            {
                // enhance header
                model.Header.FileDescription.Description.Clear(); // clear any MVD
                model.Header.FileName.AuthorizationMailingAddress.Clear();
                model.Header.FileName.AuthorizationMailingAddress.Add("info@xbim.net");
                model.Header.FileName.AuthorizationName = "";
                model.Header.FileName.AuthorName.Clear();
                model.Header.FileName.AuthorName.Add("xBIM Team");
                model.Header.FileName.Name = "IFC Rail Example";
                model.Header.FileName.Organization.Clear();
                model.Header.FileName.Organization.Add("buildingSMART");
                model.Header.FileName.OriginatingSystem = "xBIM Toolkit";
                model.Header.FileName.PreprocessorVersion = typeof(IModel).Assembly.ImageRuntimeVersion;


                var oh = i.FirstOrDefault<IfcOwnerHistory>();
                oh.OwningApplication.ApplicationDeveloper.Addresses.Clear();
                oh.OwningApplication.ApplicationDeveloper.Description = "xBIM Team";
                oh.OwningApplication.ApplicationDeveloper.Identification = "xBIM";
                oh.OwningApplication.ApplicationDeveloper.Name = "xBIM Team";
                oh.OwningApplication.ApplicationDeveloper.Roles.Clear();
                oh.OwningApplication.ApplicationFullName = "xBIM for IFC Rail";
                oh.OwningApplication.ApplicationIdentifier = "XBIM4RAIL";
                oh.OwningApplication.Version = "1.0";

                model.EntityNew += entity =>
                {
                    if (entity is IfcRoot root)
                    {
                        root.OwnerHistory = oh;
                        root.GlobalId = Guid.NewGuid();
                    }
                };


                txn.Commit();
            }
        }

        #region Delete
        /// <summary>
        /// This only keeps cache of metadata and types to speed up reflection search.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, List<ReferingType>> ReferingTypesCache =
            new ConcurrentDictionary<Type, List<ReferingType>>();

        /// <summary>
        /// This will replace the entity with another entity and will optionally remove it from model dictionary.
        /// This will replace all references in the model.
        /// Be carefull as this might take a while to check for all occurances of the object. 
        /// </summary>
        /// <param name="model">Model from which the entity should be deleted</param>
        /// <param name="entity">Entity to be replaces</param>
        /// <param name="replacement">Entity to replace first entity</param>
        /// <param name="instanceRemoval">Optional delegate to be used to remove entity from the instance collection.
        /// This should be reversable action within a transaction.</param>
        public static void Replace(IModel model, IPersistEntity entity, IPersistEntity replacement)
        {
            if (!entity.Model.Equals(replacement.Model))
                throw new XbimException("It isn't possible to replace entities from different models. Insert copy of the entity first.");

            var commonType = GetCommonAncestor(entity.ExpressType, replacement.ExpressType);
            var referingTypes = GetReferingTypes(model, commonType.Type);
            foreach (var referingType in referingTypes)
                ReplaceReferences<IPersistEntity, IPersistEntity>(model, entity, referingType, replacement);
        }


        public static MemoryModel GetCleanModel(MemoryModel model)
        {
            var clean = new MemoryModel(model.EntityFactory);
            clean.Header.FileDescription = model.Header.FileDescription;
            clean.Header.FileName = model.Header.FileName;
            clean.Header.FileSchema = model.Header.FileSchema;

            var products = model.Instances.OfType<IfcProduct>();
            var map = new XbimInstanceHandleMap(model, clean);

            // speed up inverse lookups with inverse cache
            using (model.BeginInverseCaching())
            {
                foreach (var product in products)
                {
                    clean.InsertCopy(product, map, null, true, true, true);
                }
            }
            return clean;
        }

        private static IEnumerable<ReferingType> GetReferingTypes(IModel model, Type entityType)
        {
            List<ReferingType> referingTypes;
            if (ReferingTypesCache.TryGetValue(entityType, out referingTypes))
                return referingTypes;

            referingTypes = new List<ReferingType>();
            if (!ReferingTypesCache.TryAdd(entityType, referingTypes))
            {
                //it is there already (done in another thread)
                return ReferingTypesCache[entityType];
            }

            //find all potential references
            var types = model.Metadata.Types().Where(t => typeof(IInstantiableEntity).IsAssignableFrom(t.Type));

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var type in types)
            {
                var singleReferences = type.Properties.Values.Where(p =>
                    p.EntityAttribute != null && p.EntityAttribute.Order > 0 &&
                    p.PropertyInfo.PropertyType.IsAssignableFrom(entityType)).ToList();
                var listReferences =
                    type.Properties.Values.Where(p =>
                        p.EntityAttribute != null && p.EntityAttribute.Order > 0 &&
                        p.PropertyInfo.PropertyType.IsGenericType &&
                        p.PropertyInfo.PropertyType.GenericTypeArgumentIsAssignableFrom(entityType)).ToList();
                if (!singleReferences.Any() && !listReferences.Any()) continue;

                referingTypes.Add(new ReferingType { Type = type, SingleReferences = singleReferences, ListReferences = listReferences });
            }
            return referingTypes;
        }

        /// <summary>
        /// Deletes references to specified entity from all entities in the model where entity is
        /// a references as an object or as a member of a collection.
        /// </summary>
        /// <param name="model">Model to be used</param>
        /// <param name="entity">Entity to be removed from references</param>
        /// <param name="referingType">Candidate type containing reference to the type of entity</param>
        /// <param name="replacement">New reference. If this is null it just removes references to entity</param>
        private static void ReplaceReferences<TEntity, TReplacement>(IModel model, TEntity entity, ReferingType referingType, TReplacement replacement)
            where TEntity : IPersistEntity where TReplacement : TEntity
        {
            if (entity == null)
                return;

            //get all instances of this type and nullify and remove the entity
            var entitiesToCheck = model.Instances.OfType(referingType.Type.Type.Name, true);
            foreach (var toCheck in entitiesToCheck)
            {
                //check properties
                foreach (var pInfo in referingType.SingleReferences.Select(p => p.PropertyInfo))
                {
                    var pVal = pInfo.GetValue(toCheck);
                    if (pVal == null && replacement == null)
                        continue;

                    //it is enough to compare references
                    if (!ReferenceEquals(pVal, entity)) continue;
                    pInfo.SetValue(toCheck, replacement);
                }

                foreach (var pInfo in referingType.ListReferences.Select(p => p.PropertyInfo))
                {
                    var pVal = pInfo.GetValue(toCheck);
                    if (pVal == null) continue;

                    //it might be uninitialized optional item set
                    var optSet = pVal as IOptionalItemSet;
                    if (optSet != null && !optSet.Initialized) continue;

                    //or it is non-optional item set implementing IList
                    var itemSet = pVal as IList;
                    if (itemSet != null)
                    {
                        if (itemSet.Contains(entity))
                            itemSet.Remove(entity);
                        if (replacement != null)
                            itemSet.Add(replacement);
                        continue;
                    }

                    //fall back operating on common list functions using reflection (this is slow)
                    var contMethod = pInfo.PropertyType.GetMethod("Contains");
                    if (contMethod == null)
                    {
                        var msg =
                            string.Format(
                                "It wasn't possible to check containment of entity {0} in property {1} of {2}. No suitable method found.",
                                entity.GetType().Name, pInfo.Name, toCheck.GetType().Name);
                        throw new XbimException(msg);
                    }
                    var contains = (bool)contMethod.Invoke(pVal, new object[] { entity });
                    if (!contains) continue;
                    var removeMethod = pInfo.PropertyType.GetMethod("Remove");
                    if (removeMethod == null)
                    {
                        var msg =
                            string.Format(
                                "It wasn't possible to remove reference to entity {0} in property {1} of {2}. No suitable method found.",
                                entity.GetType().Name, pInfo.Name, toCheck.GetType().Name);
                        throw new XbimException(msg);
                    }
                    removeMethod.Invoke(pVal, new object[] { entity });

                    if (replacement == null)
                        continue;

                    var addMethod = pInfo.PropertyType.GetMethod("Add");
                    if (addMethod == null)
                    {
                        var msg =
                            string.Format(
                                "It wasn't possible to add reference to entity {0} in property {1} of {2}. No suitable method found.",
                                entity.GetType().Name, pInfo.Name, toCheck.GetType().Name);
                        throw new XbimException(msg);
                    }
                    addMethod.Invoke(pVal, new object[] { replacement });
                }
            }

        }

        /// <summary>
        /// Helper structure to hold information for reference removal. If multiple objects of the same type are to
        /// be removed this will cache the information about where to have a look for the references.
        /// </summary>
        private struct ReferingType
        {
            public ExpressType Type;
            public List<ExpressMetaProperty> SingleReferences;
            public List<ExpressMetaProperty> ListReferences;
        }
        #endregion

        private static ExpressType GetCommonAncestor(ExpressType a, ExpressType b)
        {
            if (a == b)
                return a;

            // if required output type is different to input type we should find a common ancestor
            var found = false;
            while (a != null)
            {

                var outExpress = b;
                while (outExpress != null)
                {
                    if (outExpress == a)
                    {
                        a = outExpress;
                        found = true;
                        break;
                    }
                    outExpress = outExpress.SuperType;
                }
                if (found)
                    break;
                a = a.SuperType;
            }
            return a;
        }

        #region Insert
        /// <summary>
        /// Inserts shallow copy of an object into the same model. The entity must originate from the same schema (the same EntityFactory). 
        /// This operation happens within a transaction which you have to handle yourself unless you set the parameter "noTransaction" to true.
        /// 
        /// </summary>
        /// <typeparam name="TIn">Type of the copied entity</typeparam>
        /// <typeparam name="TOut">Prefered output type. Should have shared ancestor at some level with the input type</typeparam>
        /// <param name="model">Model to be used as a target</param>
        /// <param name="toCopy">Entity to be copied</param>
        /// <returns>Copy from this model</returns>
        public static TOut InsertCopy<TOut>(IModel model, IPersistEntity toCopy) where TOut : IPersistEntity, IInstantiableEntity
        {
            try
            {
                var expressType = GetCommonAncestor(model.Metadata.ExpressType(toCopy), model.Metadata.ExpressType(typeof(TOut)));
                var copy = model.Instances.New<TOut>();

                var props = expressType.Properties.Values.Where(p => !p.EntityAttribute.IsDerived);
                foreach (var prop in props)
                {
                    var value = prop.PropertyInfo.GetValue(toCopy, null);
                    if (value == null) continue;

                    var isInverse = (prop.EntityAttribute.Order == -1); //don't try and set the values for inverses
                    var theType = value.GetType();
                    //if it is an express type or a value type, set the value
                    if (theType.IsValueType || typeof(ExpressType).IsAssignableFrom(theType) ||
                        theType == typeof(string))
                    {
                        prop.PropertyInfo.SetValue(copy, value, null);
                    }
                    else if (!isInverse && typeof(IPersistEntity).IsAssignableFrom(theType))
                    {
                        prop.PropertyInfo.SetValue(copy, value, null);
                    }
                    else if (!isInverse && typeof(IList).IsAssignableFrom(theType))
                    {
                        var itemType = theType.GetItemTypeFromGenericType();

                        var copyColl = prop.PropertyInfo.GetValue(copy, null) as IList;
                        if (copyColl == null)
                            throw new Exception(string.Format("Unexpected collection type ({0}) found", itemType.Name));

                        foreach (var item in (IList)value)
                        {
                            var actualItemType = item.GetType();
                            if (actualItemType.IsValueType || typeof(ExpressType).IsAssignableFrom(actualItemType))
                                copyColl.Add(item);
                            else if (typeof(IPersistEntity).IsAssignableFrom(actualItemType))
                            {
                                copyColl.Add(item);
                            }
                            else if (typeof(IList).IsAssignableFrom(actualItemType)) //list of lists
                            {
                                var listColl = (IList)item;
                                var getAt = copyColl.GetType().GetMethod("GetAt");
                                if (getAt == null) throw new Exception(string.Format("GetAt Method not found on ({0}) found", copyColl.GetType().Name));
                                var copyListColl = getAt.Invoke(copyColl, new object[] { copyColl.Count }) as IList;
                                if (copyListColl == null)
                                    throw new XbimException("Collection can't be used as IList");
                                foreach (var listItem in listColl)
                                {
                                    var actualListItemType = listItem.GetType();
                                    if (actualListItemType.IsValueType ||
                                        typeof(ExpressType).IsAssignableFrom(actualListItemType))
                                        copyListColl.Add(listItem);
                                    else if (typeof(IPersistEntity).IsAssignableFrom(actualListItemType))
                                    {
                                        copyListColl.Add(listItem);
                                    }
                                    else
                                        throw new Exception(string.Format("Unexpected collection item type ({0}) found",
                                            itemType.Name));
                                }
                            }
                            else
                                throw new Exception(string.Format("Unexpected collection item type ({0}) found",
                                    itemType.Name));
                        }
                    }
                    else
                        throw new Exception(string.Format("Unexpected item type ({0})  found", theType.Name));
                }
                return copy;
            }
            catch (Exception e)
            {
                throw new XbimException(string.Format("General failure in InsertCopy ({0})", e.Message), e);
            }
        }
        #endregion
    }
}
