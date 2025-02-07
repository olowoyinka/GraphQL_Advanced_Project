﻿using GraphQL_UserBoarding.Data;
using GraphQL_UserBoarding.InputTypes;
using GraphQL_UserBoarding.Models;
using GraphQL_UserBoarding.Shared;
using GraphQL_UserBoarding.ViewModels;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GraphQL_UserBoarding.Logics
{
    public class AuthLogic : IAuthLogic
    {
		private readonly AppDbContext _context;

		private readonly TokenSettings _tokenSettings;

		public AuthLogic(AppDbContext context,
							IOptions<TokenSettings> tokenSettings)
        {
			_context = context;
			_tokenSettings = tokenSettings.Value;
		}

		public string Register(RegisterInputType registerInput)
		{
			string errorMessage = RegistrationValidations(registerInput);
			if (!string.IsNullOrEmpty(errorMessage))
			{
				return errorMessage;
			}

			var newUser = new User
			{
				EmailAddress = registerInput.EmailAddress,
				FirstName = registerInput.FirstName,
				LastName = registerInput.LastName,
				Password = PasswordHash(registerInput.ConfirmPassword)
			};

			_context.User.Add(newUser);
			_context.SaveChanges();

			// default role on registration
			var newUserRoles = new UserRoles
			{
				Name = "admin",
				UserId = newUser.UserId
			};

			_context.UserRoles.Add(newUserRoles);
			_context.SaveChanges();

			return "Registration success";
		}

		public TokenResponseModel Login(LoginInputType loginInput)
		{
			var result = new TokenResponseModel { Message = "Success" };
			if (string.IsNullOrEmpty(loginInput.Email)
			|| string.IsNullOrEmpty(loginInput.Password))
			{
				result.Message = "Invalid Credentials";
				return result;
			}

			var user = _context.User.Where(_ => _.EmailAddress == loginInput.Email).FirstOrDefault();
			if (user == null)
			{
				result.Message = "Invalid Credentials";
				return result;
			}

			if (!ValidatePasswordHash(loginInput.Password, user.Password))
			{
				result.Message = "Invalid Credentials";
				return result;
			}

			var roles = _context.UserRoles.Where(_ => _.UserId == user.UserId).ToList();

			result.AccessToken = GetJWTAuthKey(user, roles);

			result.RefreshToken = GenerateRefreshToken();

			user.RefreshToken = result.RefreshToken;
			user.RefershTokenExpiration = DateTime.Now.AddDays(7);
			_context.SaveChanges();

			return result;
		}


		public TokenResponseModel RenewAccessToken(RenewTokenInputType renewToken)
		{
			var result = new TokenResponseModel { Message = "Success" };

			ClaimsPrincipal principal = GetClaimsFromExpiredToken(renewToken.AccessToken);

			if (principal == null)
			{
				result.Message = "Invalid Token";
				return result;
			}

			string email = principal.Claims.Where(_ => _.Type == "Email").Select(_ => _.Value).FirstOrDefault();

			if (string.IsNullOrEmpty(email))
			{
				result.Message = "Invalid Token";
				return result;
			}

			var user = _context.User.Where(_ => _.EmailAddress == email && 
												_.RefreshToken == renewToken.RefreshToken && 
												_.RefershTokenExpiration > DateTime.Now).FirstOrDefault();
			if (user == null)
			{
				result.Message = "Invalid Token";
				return result;
			}

			var userRoles = _context.UserRoles.Where(_ => _.UserId == user.UserId).ToList();

			result.AccessToken = GetJWTAuthKey(user, userRoles);

			result.RefreshToken = GenerateRefreshToken();

			user.RefreshToken = result.RefreshToken;
			user.RefershTokenExpiration = DateTime.Now.AddDays(7);

			_context.SaveChanges();

			return result;
		}


		private string RegistrationValidations(RegisterInputType registerInput)
		{
			if (string.IsNullOrEmpty(registerInput.EmailAddress))
			{
				return "Email can't be empty";
			}

			if (string.IsNullOrEmpty(registerInput.Password) || string.IsNullOrEmpty(registerInput.ConfirmPassword))
			{
				return "Password Or ConfirmPasswor Can't be empty";
			}

			if (registerInput.Password != registerInput.ConfirmPassword)
			{
				return "Invalid confirm password";
			}

			string emailRules = @"[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?";

			if (!Regex.IsMatch(registerInput.EmailAddress, emailRules))
			{
				return "Not a valid email";
			}

			// atleast one lower case letter
			// atleast one upper case letter
			// atleast one special character
			// atleast one number
			// atleast 8 character length
			string passwordRules = @"^.*(?=.{8,})(?=.*\d)(?=.*[a-z])(?=.*[A-Z])(?=.*[!*@#$%^&+=]).*$";

			if (!Regex.IsMatch(registerInput.Password, passwordRules))
			{
				return "Not a valid password";
			}

			return string.Empty;
		}


		private string PasswordHash(string password)
		{
			byte[] salt;
			new RNGCryptoServiceProvider().GetBytes(salt = new byte[16]);

			var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 1000);
			byte[] hash = pbkdf2.GetBytes(20);

			byte[] hashBytes = new byte[36];
			Array.Copy(salt, 0, hashBytes, 0, 16);
			Array.Copy(hash, 0, hashBytes, 16, 20);

			return Convert.ToBase64String(hashBytes);
		}

		private bool ValidatePasswordHash(string password, string dbPassword)
		{
			byte[] hashBytes = Convert.FromBase64String(dbPassword);

			byte[] salt = new byte[16];
			Array.Copy(hashBytes, 0, salt, 0, 16);

			var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 1000);
			byte[] hash = pbkdf2.GetBytes(20);

			for (int i = 0; i < 20; i++)
			{
				if (hashBytes[i + 16] != hash[i])
				{
					return false;
				}
			}

			return true;
		}

		private string GetJWTAuthKey(User user, List<UserRoles> roles)
		{
			var securtityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_tokenSettings.Key));

			var credentials = new SigningCredentials(securtityKey, SecurityAlgorithms.HmacSha256);

			var claims = new List<Claim>();

			claims.Add(new Claim("Email", user.EmailAddress));
			claims.Add(new Claim("LastName", user.LastName));
			if ((roles?.Count ?? 0) > 0)
			{
				foreach (var role in roles)
				{
					claims.Add(new Claim(ClaimTypes.Role, role.Name));
				}
			}

			var jwtSecurityToken = new JwtSecurityToken(
				issuer: _tokenSettings.Issuer,
				audience: _tokenSettings.Audience,
				expires: DateTime.Now.AddSeconds(30),
				signingCredentials: credentials,
				claims: claims
			);

			return new JwtSecurityTokenHandler().WriteToken(jwtSecurityToken);
		}


		private string GenerateRefreshToken()
		{
			var randomNumber = new byte[32];

			using (var generator = RandomNumberGenerator.Create())
			{
				generator.GetBytes(randomNumber);
				return Convert.ToBase64String(randomNumber);
			}
		}

		private ClaimsPrincipal GetClaimsFromExpiredToken(string accessToken)
		{
			var tokenValidationParameter = new TokenValidationParameters
			{
				ValidIssuer = _tokenSettings.Issuer,
				ValidateIssuer = true,
				ValidAudience = _tokenSettings.Audience,
				ValidateAudience = true,
				IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_tokenSettings.Key)),
				ValidateLifetime = false // ignore expiration
			};

			var jwtHandler = new JwtSecurityTokenHandler();
			var principal = jwtHandler.ValidateToken(accessToken, tokenValidationParameter, out SecurityToken securityToken);

			var jwtScurityToken = securityToken as JwtSecurityToken;
			if (jwtScurityToken == null)
			{
				return null;
			}

			return principal;
		}
	}
}