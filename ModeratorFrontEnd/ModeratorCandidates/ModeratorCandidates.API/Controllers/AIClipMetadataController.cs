﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ModeratorCandidates.API.Services;
using ModeratorCandidates.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ModeratorCandidates.API.Controllers
{
	// TODO: Many design decisions still need to be made and implemented:
	// 1. What is the authentication/authorization scheme here going forward?
	// 2. Do the calls to the service need to be async? That will probably depend on how the data component is implemented.
	// 3. Do we need to provide a filtering component to allow moderators to further refine their experience (i.e. restrict to certain days, certain hydrophone locations, etc.)?
	// 4. How many moderators will there be? Do we need to be concerned about overlapping/conflicting sessions?

	[Route("api/aiclipmetadata")]
	[ApiController]
	public class AIClipMetadataController : ControllerBase
	{
		private readonly MetadataRepository repo;

		public AIClipMetadataController(MetadataRepository repo)
		{
			this.repo = repo;
		}

		private async Task<IQueryable<AIClipMetadata>> GetMappedClipData()
		{
			return (await repo.GetAll())
				.Select(x => MapMetadataToAIClipMetadata(x))
				.AsQueryable();
		}

		private IQueryable<AIClipMetadata> ApplyFilters(IQueryable<AIClipMetadata> queryable, Pagination pagination)
		{
			if (pagination.timeframe.ToLower() != "all")
			{
				var now = DateTime.Now;
				if (pagination.timeframe.ToLower() == "30m")
				{
					now = now.AddMinutes(-30);
				}
				queryable = queryable.Where(x => x.timestamp >= now);
			}

			if (pagination.sortBy.ToLower() == "confidence" && pagination.sortOrder.ToLower() == "asc")
			{
				queryable = queryable.OrderBy(x => x.averageConfidence).ThenBy(x => x.timestamp).ThenBy(x => x.id);
			}

			if (pagination.sortBy.ToLower() == "confidence" && pagination.sortOrder.ToLower() == "desc")
			{
				queryable = queryable.OrderByDescending(x => x.averageConfidence).ThenBy(x => x.timestamp).ThenBy(x => x.id);
			}

			if (pagination.sortBy.ToLower() == "timestamp" && pagination.sortOrder.ToLower() == "asc")
			{
				queryable = queryable.OrderBy(x => x.timestamp).ThenByDescending(x => x.averageConfidence).ThenBy(x => x.id);
			}

			if (pagination.sortBy.ToLower() == "timestamp" && pagination.sortOrder.ToLower() == "desc")
			{
				queryable = queryable.OrderByDescending(x => x.timestamp).ThenByDescending(x => x.averageConfidence).ThenBy(x => x.id);
			}

			return queryable;
		}

		private IQueryable<AIClipMetadata> TakeRecords(IQueryable<AIClipMetadata> queryable, Pagination pagination)
		{
			return queryable
				.Skip((pagination.Page - 1) * pagination.RecordsPerPage)
				.Take(pagination.RecordsPerPage);
		}

		[HttpGet]
		[ProducesResponseType(typeof(IEnumerable<AIClipMetadata>), 200)]
		[ProducesResponseType(500)]
		public async Task<IActionResult> Get([FromQuery] Pagination pagination)
		{
			try
			{
				var queryable = await GetMappedClipData();

				queryable = ApplyFilters(queryable, pagination);

				double count = queryable.Count();

				queryable = TakeRecords(queryable, pagination);

				double totalAmountPages = Math.Ceiling(count / pagination.RecordsPerPage);

				HttpContext.Response.Headers.Add("totalNumberRecords", count.ToString());
				HttpContext.Response.Headers.Add("totalAmountPages", totalAmountPages.ToString());

				return Ok(queryable.ToList());
			}
			catch (Exception ex)
			{
				return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
			}
		}

		[HttpGet]
		[Route("unreviewed")]
		[ProducesResponseType(typeof(IEnumerable<AIClipMetadata>), 200)]
		[ProducesResponseType(500)]
		public async Task<IActionResult> GetUnreviewed([FromQuery] Pagination pagination)
		{
			try
			{
				var queryable = await GetMappedClipData();

				queryable = queryable.Where(x => x.status == "Unreviewed");

				queryable = ApplyFilters(queryable, pagination);

				double count = queryable.Count();

				queryable = TakeRecords(queryable, pagination);

				double totalAmountPages = Math.Ceiling(count / pagination.RecordsPerPage);

				HttpContext.Response.Headers.Add("totalNumberRecords", count.ToString());
				HttpContext.Response.Headers.Add("totalAmountPages", totalAmountPages.ToString());

				return Ok(queryable.ToList());
			}
			catch (Exception ex)
			{
				return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
			}
		}

		[HttpGet("{id}")]
		[ProducesResponseType(typeof(AIClipMetadata), 200)]
		[ProducesResponseType(404)]
		[ProducesResponseType(500)]
		public async Task<IActionResult> GetById(string id)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(id))
				{
					return BadRequest("No id provided");
				}
				var record = await repo.GetById(id);

				if (record == null)
				{
					return NotFound();
				}

				return Ok(MapMetadataToAIClipMetadata(record));
			}
			catch (Exception ex)
			{
				return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
			}
		}

		[HttpPut("{id}")]
		public async Task<IActionResult> Put(string id, AIClipMetadataReviewResult result)
		{
			var record = await repo.GetById(id);

			if (record == null) { return NotFound(); }

			record.comments = result.comments;
			record.moderator = result.moderator;
			record.dateModerated = result.dateModerated.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
			record.reviewed = result.status.ToLower() == "reviewed" ? true : false;
			record.SRKWFound = (string.IsNullOrWhiteSpace(result.found)) ? "no" : result.found.ToLower();

			if (!string.IsNullOrWhiteSpace(result.tags))
			{
				var tagList = new List<string>();
				tagList.AddRange(result.tags.Split(',').ToList().Select(x => x.Trim()));
				record.tags = string.Join(";", tagList);
			}

			await repo.Commit();

			return NoContent();
		}

		private AIClipMetadata MapMetadataToAIClipMetadata(Metadata item)
		{
			var metadata = new AIClipMetadata();
			metadata.id = string.IsNullOrEmpty(item.id) ? Guid.NewGuid().ToString() : item.id;
			metadata.imageUri = string.IsNullOrWhiteSpace(item.imageUri) ? "Unavailable" : item.imageUri;
			metadata.audioUri = string.IsNullOrWhiteSpace(item.audioUri) ? "Unavailable" : item.audioUri;
			metadata.status = item.reviewed ? "Reviewed" : "Unreviewed";
			metadata.averageConfidence = item.whaleFoundConfidence;
			metadata.found = string.IsNullOrWhiteSpace(item.SRKWFound) ? "No" : item.SRKWFound;
			metadata.timestamp = string.IsNullOrWhiteSpace(item.timestamp) ? DateTime.Now : DateTime.Parse(item.timestamp);
			metadata.location = new AILocation() { latitude = item.location.latitude, longitude = item.location.longitude, name = item.location.name };

			metadata.annotations = new List<AIAnnotation>();
			if (item.predictions != null && item.predictions.Count > 0)
			{
				foreach (var prediction in item.predictions)
				{
					metadata.annotations.Add(new AIAnnotation() { confidence = prediction.confidence, duration = prediction.duration, id = prediction.id, startTime = prediction.startTime });
				}
			}

			return metadata;
		}
	}
}